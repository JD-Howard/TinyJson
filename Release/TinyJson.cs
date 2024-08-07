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
            
            // all tests still pass after removing leading/trailing whitespace and making Split() short circuit on whitespace
            return (T)ParseValue(typeof(T), json.Trim(), ignoreEnumCase);
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
                char ch = json[i];
                if (char.IsWhiteSpace(ch))
                    continue;
                
                switch (ch)
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

            Nullable<bool> da = false;
            var nestedType = Nullable.GetUnderlyingType(type);
            var isNullable = nestedType != null;
            if (isNullable)
                type = nestedType;
            
            if (type.IsPrimitive)
                return ParsePrimitiveValue(type, json, isNullable);
            
            if (type.IsEnum)
                return ParseEnumValue(type, json, ignoreEnumCase, isNullable);
            
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

        private static object ParseEnumValue(Type type, string json, bool ignoreEnumCase, bool isNullable)
        {
            if (json.FirstOrDefault() == '"')
                json = ParseStringValue(json);
            try
            {
                return Enum.Parse(type, json, ignoreEnumCase); // This will handle a name or value
            }
            catch
            {
                return isNullable ? null : Activator.CreateInstance(type); // creates the default value for Enum
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

            if (type != typeof(char))
                return null;

            if (json.Length == 1)
                return json[0];

            if (isNullable && json == NullValue)
                return null;

            var ch = ParseStringValue(json);
            return isNullable && string.IsNullOrEmpty(ch) ? (char?)null : ch.FirstOrDefault();
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
        

        private static string ParseStringValue(string json)
        {
            if (json.Length <= 2)
                return string.Empty;
            
            if (json == NullValue) // Issue #48
                return null;
                
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
                return ParseStringValue(json); // Issue #29
            
            if (char.IsDigit(json[0]) || json[0] == '-')
            {
                if (json.Contains("."))
                    return double.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl) ? dbl : (double?)null;
                
                if (long.TryParse(json, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) == false) 
                    return null;
                
                if (num > int.MaxValue || num < int.MinValue) 
                    return num; // issue #32: enable long return to prevent data loss
                
                return (int) num;
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
namespace TinyJson
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Text;
    
    //Really simple JSON writer
    //- Outputs JSON structures from an object
    //- Really simple API (new List<int> { 1, 2, 3 }).ToJson() == "[1,2,3]"
    //- Will only output public fields and property getters on objects
    public static partial class Serialization
    {
        public static string ToJson(object item, bool includeNulls, bool includeTabs)
        {
            StringBuilder strBuilder = new StringBuilder();
            AppendValue(strBuilder, item, includeNulls);
            return includeTabs 
                 ? ApplyIndentedFormatting(strBuilder.ToString())
                 : strBuilder.ToString();
        }

        private static void AppendValue(StringBuilder strBuilder, object item, bool includeNulls)
        {
            Type type = item?.GetType();
            
            if (type is null)
                strBuilder.Append("null");
            else if (type.IsPrimitive)
                AppendPrimitiveValue(strBuilder, ref item);
            else if (type.IsEnum)
                strBuilder.Append($"\"{item}\"");
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                AppendDictionaryValue(strBuilder, (IDictionary)item, type, includeNulls);
            else switch (item)
            {
                case string str:
                    AppendStringValue(strBuilder, str); 
                    break;
                case decimal dec: // Not captured by IsPrimitive
                    strBuilder.Append(dec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case DateTime date:
                    strBuilder.Append($"\"{date:O}\""); // use ISO 8601 format to preserve context
                    break;
                case DateTimeOffset offset:
                    strBuilder.Append($"\"{offset:c}\"");
                    break;
                case TimeSpan time:
                    strBuilder.Append($"\"{time:c}\"");
                    break;
                case Guid guid:
                    strBuilder.Append($"\"{guid}\"");
                    break;
                case IList list:
                    AppendIListValue(strBuilder, list, includeNulls);
                    break;
                default:
                    AppendObjectValue(strBuilder, item, type, includeNulls);
                    break;
            }
        }

        private static void AppendObjectValue(StringBuilder strBuilder, object item, Type type, bool includeNulls)
        {
            strBuilder.Append('{');

            bool isFirst = true;
            FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < fieldInfos.Length; i++)
            {
                if (fieldInfos[i].IsDefined(typeof(IgnoreDataMemberAttribute), true))
                    continue;

                object value = fieldInfos[i].GetValue(item);
                if (includeNulls || value != null)
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        strBuilder.Append(',');
                    
                    strBuilder.Append($"\"{GetMemberName(fieldInfos[i])}\":");
                    AppendValue(strBuilder, value, includeNulls);
                }
            }
            PropertyInfo[] propertyInfo = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < propertyInfo.Length; i++)
            {
                if (!propertyInfo[i].CanRead || propertyInfo[i].IsDefined(typeof(IgnoreDataMemberAttribute), true))
                    continue;

                object value = propertyInfo[i].GetValue(item, null);
                if (includeNulls || value != null)
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        strBuilder.Append(',');
                    
                    strBuilder.Append($"\"{GetMemberName(propertyInfo[i])}\":");
                    AppendValue(strBuilder, value, includeNulls);
                }
            }

            strBuilder.Append('}');
        }

        private static void AppendDictionaryValue(StringBuilder strBuilder, IDictionary dict, Type type, bool includeNulls)
        {
            Type keyType = type.GetGenericArguments()[0];

            var isStringKey = keyType == typeof(string);
            var isDecimalKey = keyType == typeof(decimal);
            if (!isStringKey && !keyType.IsPrimitive && !keyType.IsEnum && !isDecimalKey && !SpecialKeys.Contains(keyType))
            {
                strBuilder.Append("{}");
                return;
            }

            strBuilder.Append('{');
            bool isWrapped = keyType.IsEnum || SpecialKeys.Contains(keyType);
            bool isFirst = true;
            foreach (object key in dict.Keys)
            {
                if (isFirst)
                    isFirst = false;
                else
                    strBuilder.Append(',');
                
                if (isStringKey)
                {
                    AppendStringValue(strBuilder, key.ToString());
                    strBuilder.Append(':');
                }
                else
                { 
                    if (isWrapped)
                        AppendValue(strBuilder, key, false);
                    else
                    {
                        // handles types that do not already get value-appended wrapped in quotes; int, float, etc
                        strBuilder.Append('"');
                        AppendValue(strBuilder, key, false);
                        strBuilder.Append('"');
                    }
                    strBuilder.Append(':');
                }
                
                AppendValue(strBuilder, dict[key], includeNulls);
            }
            strBuilder.Append('}');
        }

        private static void AppendIListValue(StringBuilder strBuilder, IList list, bool includeNulls)
        {
            strBuilder.Append('[');
            bool isFirst = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (isFirst)
                    isFirst = false;
                else
                    strBuilder.Append(',');
                AppendValue(strBuilder, list[i], includeNulls);
            }
            strBuilder.Append(']');
        }

        private static void AppendStringValue(StringBuilder strBuilder, string str)
        {
            strBuilder.Append('"');
            for (int i = 0; i < str.Length; ++i)
                if (str[i] < ' ' || str[i] == '"' || str[i] == '\\')
                {
                    strBuilder.Append('\\');
                    int j = "\"\\\n\r\t\b\f".IndexOf(str[i]);
                    if (j >= 0)
                        strBuilder.Append("\"\\nrtbf"[j]);
                    else
                        strBuilder.AppendFormat("u{0:X4}", (UInt32)str[i]);
                }
                else
                    strBuilder.Append(str[i]);
            strBuilder.Append('"');
        }

        private static void AppendPrimitiveValue(StringBuilder strBuilder, ref object item)
        {
            switch (item)
            {
                case float flt:
                    strBuilder.Append(flt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case double dbl:
                    strBuilder.Append(dbl.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case bool bln:
                    strBuilder.Append(bln ? "true" : "false");
                    break;
                case char chr:
                    AppendStringValue(strBuilder, $"{chr}");
                    break;
                default:
                    strBuilder.Append(item); // all other number types can just auto-ToString()
                    break;
            }
        }

        private static string GetMemberName(MemberInfo member)
        {
            if (member.IsDefined(typeof(DataMemberAttribute), true))
            {
                DataMemberAttribute dataMemberAttribute = (DataMemberAttribute)Attribute.GetCustomAttribute(member, typeof(DataMemberAttribute), true);
                if (!string.IsNullOrEmpty(dataMemberAttribute.Name))
                    return dataMemberAttribute.Name;
            }

            return member.Name;
        }
        
        
        private static string ApplyIndentedFormatting(string unformattedJson)
        {
            var chars = unformattedJson.ToCharArray();
            var newStringBuilder = new StringBuilder();
            var tabs = 0;

            //char? chrRight;
            char? chrLeft = null;
            for (var i = 0; i < chars.Length; i++)
            {
                var chr = chars[i];
                switch (chr)
                {
                    case '[':
                        if (chrLeft is ',')
                        {
                            newStringBuilder.Append('\n');
                            newStringBuilder.Append($"{GetTabs(tabs)}{chr}");
                        }
                        else
                        {
                            newStringBuilder.Append(chr);
                        }

                        tabs++;
                        break;
                    case ']':
                        tabs--;
                        newStringBuilder.Append($"\n{GetTabs(tabs)}{chr}");
                        break;
                    case '{':
                        if (chrLeft.HasValue && (chrLeft == ',' || chrLeft == '['))
                        {
                            newStringBuilder.Append('\n');
                            newStringBuilder.Append($"{GetTabs(tabs)}{chr}");
                        }
                        else
                        {
                            newStringBuilder.Append(chr);
                        }

                        tabs++;
                        break;
                    case '}':
                        tabs--;
                        newStringBuilder.Append($"\n{GetTabs(tabs)}{chr}");
                        break;
                    case '"':
                        if (chrLeft.HasValue && (chrLeft == '{' || chrLeft == '[' || chrLeft == ',' || chrLeft == ':'))
                        {
                            if (chrLeft != ':')
                            {
                                newStringBuilder.Append('\n');
                                newStringBuilder.Append($"{GetTabs(tabs)}{chr}");
                            }
                            else
                            {
                                newStringBuilder.Append(chr);
                            }


                            var nextIndex = GetNextChar(chars, i + 1);
                            newStringBuilder.Append(new string(chars, i + 1, nextIndex - i - 1));
                            i = nextIndex - 1;
                            chr = chars[i];
                        }
                        else
                        {
                            newStringBuilder.Append(chr);
                        }

                        break;
                    default:
                        newStringBuilder.Append(chr);
                        break;
                }


                chrLeft = chr;
            }
            return newStringBuilder.ToString();
        }
        
        private static string GetTabs(int level)
        {
            if (level == 0)
                return null;

            var chars = new char[level];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = '\t';
            }

            return new string(chars);
        }

        private static int GetNextChar(char[] chars, in int start)
        {
            for (var i = start; i < chars.Length; i++)
            {
                var chr = chars[i];
                if (chr == '"' && chars[i - 2] != '\\')
                    return i;
            }

            return -1;
        }
        
    }
}
namespace System
{   
    public static class TinyJsonSystemExtensions
    {
        /// <summary>
        /// Extension method for parsing JSON string or JSON file to the specified type using TinyJson library.
        /// </summary>
        /// <typeparam name="T">The type to parse the JSON into. Using &lt;object&gt; produces a Dictionary&lt;string,object&gt; representation.</typeparam>
        /// <param name="rawJsonOrFilepath">The JSON string or JSON file path.</param>
        /// <param name="ignoreEnumCase">Optional parameter to ignore case sensitivity when parsing enumeration types.</param>
        /// <returns>The parsed object of type T.</returns>
        public static T TinyJsonParse<T>(this string rawJsonOrFilepath, bool ignoreEnumCase = true)
        {
            return IO.File.Exists(rawJsonOrFilepath)
                 ? TinyJson.Serialization.FromJson<T>(IO.File.ReadAllText(rawJsonOrFilepath), ignoreEnumCase)
                 : TinyJson.Serialization.FromJson<T>(rawJsonOrFilepath, ignoreEnumCase);
        }

        /// <summary>
        /// Extension method for converting an object to JSON string using the TinyJson library.
        /// </summary>
        /// <param name="item">The object to be converted to JSON.</param>
        /// <param name="includeNulls">Optional parameter to include null values in the JSON string. False produces smaller JSON</param>
        /// <returns>The JSON string representation of the object.</returns>
        public static string TinyJsonConvert(this object item, bool includeNulls = false)
            => TinyJson.Serialization.ToJson(item, includeNulls, false);

        /// <summary>
        /// Extension method for converting an object to a JSON string with tab indentation using the TinyJson library.
        /// </summary>
        /// <inheritdoc cref="TinyJsonConvert(object, bool)"/>
        public static string TinyJsonTabConvert(this object item, bool includeNulls = false)
            => TinyJson.Serialization.ToJson(item, includeNulls, true);

        /// <summary>
        /// Extension method for converting an object to JSON file using the TinyJson library.
        /// </summary>
        /// <param name="item">The object to be converted to JSON.</param>
        /// <param name="filePath">The file location where object JSON should be written.</param>
        /// <param name="includeNulls">Optional parameter to include null values in the JSON string. False produces smaller JSON</param>
        /// <returns>True if the object was successfully converted and written to the provided file path.</returns>
        public static bool TinyJsonConvert(this object item, string filePath, bool includeNulls = false) 
            => TinyJsonWriter(filePath, TinyJsonConvert(item, includeNulls));

        /// <summary>
        /// Extension method for converting an object to JSON file with tab indentation using the TinyJson library.
        /// </summary>
        /// <inheritdoc cref="TinyJsonConvert(object, string, bool)"/>
        public static bool TinyJsonTabConvert(this object item, string filePath, bool includeNulls = false) 
            => TinyJsonWriter(filePath, TinyJsonTabConvert(item, includeNulls));

        private static bool TinyJsonWriter(string filePath, string json)
        {
            try
            {
                using var writer = new IO.StreamWriter(filePath, false, Text.Encoding.UTF8);
                writer.Write(json);
                return true; 
            }
            catch { return false; }
        }
    }
}
