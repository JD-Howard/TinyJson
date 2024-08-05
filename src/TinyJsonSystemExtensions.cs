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
        public static string TinyJsonTabConvert(this object item, bool includeNulls = true)
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