namespace TinyJson
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Text;
    
    // Really simple JSON parser in ~300 lines
    // - Attempts to parse JSON files with minimal GC allocation
    // - Nice and simple "[1,2,3]".FromJson<List<int>>() API
    // - Classes and structs can be parsed too!
    //      class Foo { public int Value; }
    //      "{\"Value\":10}".FromJson<Foo>()
    // - Can parse JSON without type information into Dictionary<string,object> and List<object> e.g.
    //      "[1,2,3]".FromJson<object>().GetType() == typeof(List<object>)
    //      "{\"Value\":10}".FromJson<object>().GetType() == typeof(Dictionary<string,object>)
    // - No JIT Emit support to support AOT compilation on iOS
    // - Attempts are made to NOT throw an exception if the JSON is corrupted or invalid: returns null instead.
    // - Only public fields and property setters on classes/structs will be written to
    //
    // Limitations:
    // - No JIT Emit support to parse structures quickly
    // - Limited to parsing <2GB JSON files (due to int.MaxValue)
    // - Parsing of abstract classes or interfaces is NOT supported and will throw an exception.
    public static partial class Serialization
    {
        [ThreadStatic] static Stack<List<string>> splitArrayPool;
        [ThreadStatic] static StringBuilder stringBuilder;
        [ThreadStatic] static Dictionary<Type, Dictionary<string, FieldInfo>> fieldInfoCache;
        [ThreadStatic] static Dictionary<Type, Dictionary<string, PropertyInfo>> propertyInfoCache;

        // All of these have handlers that will convert to/from a simple string representation
        private static readonly Type[] SpecialKeys = new[] {typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan)};

        public static T FromJson<T>(string json, bool ignoreEnumCase)
        {
            // Initialize, if needed, the ThreadStatic variables
            propertyInfoCache ??= new Dictionary<Type, Dictionary<string, PropertyInfo>>();
            fieldInfoCache ??= new Dictionary<Type, Dictionary<string, FieldInfo>>();
            stringBuilder ??= new StringBuilder();
            splitArrayPool ??= new Stack<List<string>>();

            //Remove all whitespace not within strings to make parsing simpler
            stringBuilder.Length = 0;
            
            // The removal of whitespace adds an average of 60ms on the "PerformanceTest" 
            // This problem scales relative to the size of the json being parsed. 
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    i = AppendUntilStringEnd(true, i, json);
                    continue;
                }
                if (char.IsWhiteSpace(c))
                    continue;

                stringBuilder.Append(c);
            }

            //Parse the thing!
            return (T)ParseValue(typeof(T), stringBuilder.ToString(), ignoreEnumCase);
        }

        static int AppendUntilStringEnd(bool appendEscapeCharacter, int startIdx, string json)
        {
            stringBuilder.Append(json[startIdx]);
            for (int i = startIdx + 1; i < json.Length; i++)
            {
                char next = json[i];
                if (next == '\\')
                {
                    if (appendEscapeCharacter)
                        stringBuilder.Append(next);
                    stringBuilder.Append(json[++i]); //Skip next character as it is escaped
                }
                else if (next == '"')
                {
                    stringBuilder.Append(next);
                    return i;
                }
                else
                    stringBuilder.Append(next);
            }
            return json.Length - 1;
        }

        //Splits { <value>:<value>, <value>:<value> } and [ <value>, <value> ] into a list of <value> strings
        static List<string> Split(string json)
        {
            List<string> splitArray = splitArrayPool.Count > 0 ? splitArrayPool.Pop() : new List<string>();
            splitArray.Clear();
            if (json.Length == 2)
                return splitArray;
            int parseDepth = 0;
            stringBuilder.Length = 0;
            for (int i = 1; i < json.Length - 1; i++)
            {
                switch (json[i])
                {
                    case '[':
                    case '{':
                        parseDepth++;
                        break;
                    case ']':
                    case '}':
                        parseDepth--;
                        break;
                    case '"':
                        i = AppendUntilStringEnd(true, i, json);
                        continue;
                    case ',':
                    case ':':
                        if (parseDepth == 0)
                        {
                            splitArray.Add(stringBuilder.ToString());
                            stringBuilder.Length = 0;
                            continue;
                        }
                        break;
                }

                stringBuilder.Append(json[i]);
            }

            splitArray.Add(stringBuilder.ToString());

            return splitArray;
        }

        private const string NullValue = "null";
        internal static object ParseValue(Type type, string json, bool ignoreEnumCase)
        {
            if (json.Length == 0 || NullValue.Equals(json))
                return null;

            if (type.IsClass)
                goto ClassHandlers;
            
            var nestedType = Nullable.GetUnderlyingType(type);
            var isNullable = nestedType != null;
            if (isNullable)
                type = nestedType;
            
            if (type.IsPrimitive)
                return ParsePrimitiveValue(type, json, isNullable);
            
            if (type.IsEnum)
                return ParseEnumValue(type, json, ignoreEnumCase);
            
            if (type == typeof(decimal)) // Outlier not captured by Type.IsPrimitive
                return ParseNumberTypes<decimal>(decimal.TryParse, json, isNullable, NumberStyles.Float);
            
            // After testing for IsClass, handling IsPrimitive/Decimal/IsEnum, then IsValueType=True should be a struct...
            if (type.IsValueType)
                return ParseStructValue(type, json, isNullable, ignoreEnumCase);

            ClassHandlers:
            if (type == typeof(string))
                return ParseStringValue(json);
            
            if (type.IsArray)
                return ParseArrayValue(type, json, ignoreEnumCase);
            
            if (type.IsGenericType)
                return ParseGenericClass(type, json, ignoreEnumCase);
            
            if (type == typeof(object))
                return ParseAnonymousValue(json);
            
            if (json[0] == '{' && json[json.Length - 1] == '}')
                return ParseObject(type, json, ignoreEnumCase);

            return null;
        }

        private static object ParseGenericClass(Type type, string json, bool ignoreEnumCase)
        {
            if (type.GetGenericTypeDefinition() == typeof(List<>))
                return ParseListValue(type, json, ignoreEnumCase);
                
            if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return ParseDictionaryValue(type, json, ignoreEnumCase);

            // This is slightly sketchy, but tests prove capabilities with user-defined generic classes.
            // There is no support for abstract classes, but the tests do
            // cover a concrete implementation of an abstract/generic class.
            if (json[0] == '{' && json[json.Length - 1] == '}')
                return ParseObject(type, json, ignoreEnumCase);
                
            return null; // malformed?
        }

        private static object ParseDictionaryValue(Type type, string json, bool ignoreEnumCase)
        {
            Type keyType, valueType;
            {
                Type[] args = type.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
            }

            //Must be a valid dictionary element
            if (json[0] != '{' || json[json.Length - 1] != '}')
                return null;
            //The list is split into key/value pairs only, this means the split must be divisible by 2 to be valid JSON
            List<string> elems = Split(json);
            if (elems.Count % 2 != 0)
                return null;

            var isStringKey = keyType == typeof(string);
            var isDecimalKey = keyType == typeof(decimal);
            if (!isStringKey && !keyType.IsPrimitive && !keyType.IsEnum && !isDecimalKey && !SpecialKeys.Contains(keyType))
                return null;
            
            var dictionary = (IDictionary)type.GetConstructor(new Type[] { typeof(int) }).Invoke(new object[] { elems.Count / 2 });
            for (int i = 0; i < elems.Count; i += 2)
            {
                if (isStringKey && elems[i].Length < 2)
                    continue; // string.Empty is technically a valid dictionary key
                    
                if (elems[i].Length == 0)
                    continue;

                string rawKey = elems[i].FirstOrDefault() == '"'
                    ? elems[i].Substring(1, elems[i].Length - 2)
                    : elems[i];
                    
                object keyValue = isStringKey ? rawKey : ParseValue(keyType, rawKey, ignoreEnumCase);
                object val = ParseValue(valueType, elems[i + 1], ignoreEnumCase);
                dictionary[keyValue] = val;
            }
            return dictionary;
        }

        private static object ParseListValue(Type type, string json, bool ignoreEnumCase)
        {
            Type listType = type.GetGenericArguments()[0];
            if (json[0] != '[' || json[json.Length - 1] != ']')
                return null;

            List<string> elems = Split(json);
            var list = (IList)type.GetConstructor(new Type[] { typeof(int) }).Invoke(new object[] { elems.Count });
            for (int i = 0; i < elems.Count; i++)
                list.Add(ParseValue(listType, elems[i], ignoreEnumCase));
            splitArrayPool.Push(elems);
            return list;
        }

        private static object ParseArrayValue(Type type, string json, bool ignoreEnumCase)
        {
            Type arrayType = type.GetElementType();
            if (json[0] != '[' || json[json.Length - 1] != ']')
                return null;

            List<string> elems = Split(json);
            Array newArray = Array.CreateInstance(arrayType, elems.Count);
            for (int i = 0; i < elems.Count; i++)
                newArray.SetValue(ParseValue(arrayType, elems[i], ignoreEnumCase), i);
            splitArrayPool.Push(elems);
            return newArray;
        }

        private static object ParseStructValue(Type type, string json, bool isNullable, bool ignoreEnumCase)
        {
            if (type == typeof(DateTime))
                return ParseDateTypes<DateTime>(DateTime.TryParse, json.Replace("\"", ""), isNullable, DateTimeStyles.RoundtripKind);
            
            if (type == typeof(DateTimeOffset))
                return ParseDateTypes<DateTimeOffset>(DateTimeOffset.TryParse, json.Replace("\"", ""), isNullable, DateTimeStyles.RoundtripKind);
            
            if (type == typeof(TimeSpan))
                return TimeSpan.TryParse(json.Replace("\"", ""), CultureInfo.InvariantCulture, out var result) ? result : isNullable ? (TimeSpan?)null : TimeSpan.Zero;
            
            if (type == typeof(Guid))
                return Guid.TryParse(json.Replace("\"", ""), out var result) ? result : isNullable ? (Guid?)null : Guid.Empty;
                
            // Fallback is to treat uncommon structs as objects
            return ParseObject(type, json, ignoreEnumCase);
        }

        private static object ParseEnumValue(Type type, string json, bool ignoreEnumCase)
        {
            if (json.FirstOrDefault() == '"')
                json = json.Substring(1, json.Length - 2);
            try
            {
                return Enum.Parse(type, json, ignoreEnumCase); // This will handle a name or value
            }
            catch
            {
                return Activator.CreateInstance(type); // creates the default value for Enum
            }
        }

        private static object ParsePrimitiveValue(Type type, string json, bool isNullable)
        {
            if (type == typeof(bool))
                return bool.TryParse(json, out var result) ? result : isNullable ? (bool?)null : false;
            
            if (type == typeof(byte))
                return ParseNumberTypes<byte>(byte.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(sbyte))
                return ParseNumberTypes<sbyte>(sbyte.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(short))
                return ParseNumberTypes<short>(short.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(ushort))
                return ParseNumberTypes<ushort>(ushort.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(int))
                return ParseNumberTypes<int>(int.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(uint))
                return ParseNumberTypes<uint>(uint.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(long))
                return ParseNumberTypes<long>(long.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(ulong))
                return ParseNumberTypes<ulong>(ulong.TryParse, json, isNullable, NumberStyles.Integer);
            
            if (type == typeof(float))
                return ParseNumberTypes<float>(float.TryParse, json, isNullable, NumberStyles.Float);
            
            if (type == typeof(double))
                return ParseNumberTypes<double>(double.TryParse, json, isNullable, NumberStyles.Float);
            
            if (type == typeof(char))
                return json.Length == 1 ? json[0] : isNullable ? (char?)null : default(char);
            
            return null;
        }

        
        private delegate bool NumberStyleParseDelegate<T>(string s, NumberStyles style, IFormatProvider provider, out T result) where T : unmanaged;
        private static object ParseNumberTypes<T>(NumberStyleParseDelegate<T> parser, string json, bool isNullable, NumberStyles style) where T : unmanaged
        {
            if (parser(json, style, CultureInfo.InvariantCulture, out var result))
                return result;

            if (isNullable)
                return null;
            
            return default(T);
        }
        
        private delegate bool DateStyleParseDelegate<T>(string s, IFormatProvider provider, DateTimeStyles styles, out T result) where T : struct;
        private static object ParseDateTypes<T>(DateStyleParseDelegate<T> parser, string json, bool isNullable, DateTimeStyles style) where T : struct
        {
            
            if (parser(json, CultureInfo.InvariantCulture, style, out var result))
                return result;

            if (isNullable)
                return null;
            
            return default(T);
        }
        

        private static object ParseStringValue(string json)
        {
            if (json.Length <= 2)
                return string.Empty;
                
            StringBuilder parseStringBuilder = new StringBuilder(json.Length);
            for (int i = 1; i < json.Length - 1; ++i)
            {
                char curr = json[i];
                char next = i + 1 < json.Length - 1 ? json[i + 1] : default;
                if (curr == '\\')
                {
                    int j = "\"\\nrtbf/".IndexOf(next);
                    if (j >= 0)
                    {
                        parseStringBuilder.Append("\"\\\n\r\t\b\f/"[j]);
                        ++i;
                        continue;
                    }
                    if (next == 'u' && i + 5 < json.Length - 1)
                    {
                        UInt32 c = 0;
                        if (UInt32.TryParse(json.Substring(i + 2, 4), NumberStyles.AllowHexSpecifier, null, out c))
                        {
                            parseStringBuilder.Append((char)c);
                            i += 5;
                            continue;
                        }
                    }
                }
                parseStringBuilder.Append(curr);
            }
            return parseStringBuilder.ToString();
        }

        static object ParseAnonymousValue(string json)
        {
            if (json.Length == 0)
                return null;
            if (json[0] == '{' && json[json.Length - 1] == '}')
            {
                List<string> elems = Split(json);
                if (elems.Count % 2 != 0)
                    return null;
                var dict = new Dictionary<string, object>(elems.Count / 2);
                for (int i = 0; i < elems.Count; i += 2)
                    dict[elems[i].Substring(1, elems[i].Length - 2)] = ParseAnonymousValue(elems[i + 1]);
                return dict;
            }
            if (json[0] == '[' && json[json.Length - 1] == ']')
            {
                List<string> items = Split(json);
                var finalList = new List<object>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    finalList.Add(ParseAnonymousValue(items[i]));
                return finalList;
            }
            
            if (json[0] == '"' && json[json.Length - 1] == '"')
                return ParseStringValue(json);
            
            if (char.IsDigit(json[0]) || json[0] == '-')
            {
                if (json.Contains("."))
                {
                    double result;
                    double.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
                    return result;
                }
                else
                {
                    int result;
                    int.TryParse(json, out result);
                    return result;
                }
            }
            if (json.Equals("true", StringComparison.InvariantCultureIgnoreCase))
                return true;
            if (json.Equals("false", StringComparison.InvariantCultureIgnoreCase))
                return false;
            
            // handles json == "null" as well as invalid JSON
            return null;
        }

        static Dictionary<string, T> CreateMemberNameDictionary<T>(T[] members) where T : MemberInfo
        {
            Dictionary<string, T> nameToMember = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < members.Length; i++)
            {
                T member = members[i];
                if (member.IsDefined(typeof(IgnoreDataMemberAttribute), true))
                    continue;

                string name = member.Name;
                if (member.IsDefined(typeof(DataMemberAttribute), true))
                {
                    DataMemberAttribute dataMemberAttribute = (DataMemberAttribute)Attribute.GetCustomAttribute(member, typeof(DataMemberAttribute), true);
                    if (!string.IsNullOrEmpty(dataMemberAttribute.Name))
                        name = dataMemberAttribute.Name;
                }

                nameToMember.Add(name, member);
            }

            return nameToMember;
        }

        static object ParseObject(Type type, string json, bool ignoreEnumCase)
        {
            object instance = Activator.CreateInstance(type);

            //The list is split into key/value pairs only, this means the split must be divisible by 2 to be valid JSON
            List<string> elems = Split(json);
            if (elems.Count % 2 != 0)
                return instance;

            Dictionary<string, FieldInfo> nameToField;
            Dictionary<string, PropertyInfo> nameToProperty;
            if (!fieldInfoCache.TryGetValue(type, out nameToField))
            {
                nameToField = CreateMemberNameDictionary(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy));
                fieldInfoCache.Add(type, nameToField);
            }
            if (!propertyInfoCache.TryGetValue(type, out nameToProperty))
            {
                nameToProperty = CreateMemberNameDictionary(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy));
                propertyInfoCache.Add(type, nameToProperty);
            }

            foreach (var property in nameToProperty)
            {
                var propertyInfo = property.Value;
                if (propertyInfo.IsDefined(typeof(DefaultValueAttribute), true))
                {
                    var defaultValue = Attribute.GetCustomAttribute(propertyInfo, typeof(DefaultValueAttribute), true) as DefaultValueAttribute;
                    var value = defaultValue?.Value;

                    if (value != null)
                    {
                        // convert the value to the target property's type
                        value = TypeDescriptor.GetConverter(propertyInfo.PropertyType).ConvertFromString(value.ToString());
                    }

                    propertyInfo.SetValue(instance, value, null);
                }
            }
            
            for (int i = 0; i < elems.Count; i += 2)
            {
                if (elems[i].Length <= 2)
                    continue;
                string key = elems[i].Substring(1, elems[i].Length - 2);
                string value = elems[i + 1];

                FieldInfo fieldInfo;
                PropertyInfo propertyInfo;
                if (nameToField.TryGetValue(key, out fieldInfo))
                    fieldInfo.SetValue(instance, ParseValue(fieldInfo.FieldType, value, ignoreEnumCase));
                else if (nameToProperty.TryGetValue(key, out propertyInfo))
                {
                    var setMethod = propertyInfo.GetSetMethod(true);
                    if (setMethod != null && setMethod.IsPublic && !setMethod.IsStatic)
                        propertyInfo.SetValue(instance, ParseValue(propertyInfo.PropertyType, value, ignoreEnumCase), null);
                }
            }

            return instance;
        }
    }
}
