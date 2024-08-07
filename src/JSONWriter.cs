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
