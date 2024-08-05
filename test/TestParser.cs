using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TinyJson;

namespace TinyJson.Test
{
    [TestClass]
    public class TestParser
    {
        public TestParser()
        {
            // Ensures both of these have initialized so it doesn't skew single test comparisons
            Newtonsoft.Json.JsonConvert.DeserializeObject<int>("1");
            Newtonsoft.Json.JsonConvert.SerializeObject(1);
            "1".TinyJsonParse<int>().TinyJsonConvert();
        }
        
        public enum Color
        {
            Red,
            Green,
            Blue,
            Yellow
        }

        [Flags]
        public enum Style
        {
            None = 0,
            Bold = 1,
            Italic = 2,
            Underline = 4,
            Strikethrough = 8
        }

        private static long WriteMetrics(Stopwatch sw, string json, bool isNewton)
        {
            var prefix = isNewton ? "NEWT:" : "TINY:";
            var elapsed = sw.ElapsedMilliseconds;
            if (json.Length > 500)
                json = json.Substring(0, 500);
            
            Console.WriteLine($"{prefix} {sw.ElapsedMilliseconds:00000}ms from source : {json}");
            sw.Reset();
            return elapsed;
        }
        
        
        private static void Test<T>(T expected, string json, bool skipNewton = false)
        {
            var sw = new Stopwatch();
            sw.Start();
            T value1 = json.TinyJsonParse<T>();
            sw.Stop();
            Assert.AreEqual(expected, value1);
            var factor1 = WriteMetrics(sw, json, false);
            
            if (skipNewton)
                return;
            
            sw.Start();
            T value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
            sw.Stop();
            Assert.AreEqual(value2, value1);
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);
        }

        [TestMethod]
        public void TestValues()
        {
            Test(12345, "12345");
            Test(12345L, "12345");
            Test(12345UL, "12345");
            Test(12.532f, "12.532");
            Test(12.532m, "12.532");
            Test(12.532d, "12.532");
            Test("hello", "\"hello\"");
            Test("hello there", "\"hello there\"");
            Test("hello\nthere", "\"hello\nthere\"");
            Test("hello\"there", "\"hello\\\"there\"");
            Test(true, "true");
            Test(false, "false");
            Test<object>(null, "sfdoijsdfoij", true);
            Test(Color.Green, "\"Green\"");
            Test(Color.Blue, "2");
            Test(Color.Blue, "\"2\"");
            
            // Fallback scenario is by far the worst performance of the group
            // I enhanced this by not purely expecting zero to be an available fallback,
            // but my addition is only causing about 2ms of this +40m performance hit.
            Test(Color.Red, "\"sfdoijsdfoij\"", true); 
            Test(Style.Bold | Style.Italic, "\"Bold, Italic\"");
            Test(Style.Bold | Style.Italic, "3");
            Test("\u94b1\u4e0d\u591f!", "\"\u94b1\u4e0d\u591f!\"");
            Test("\u94b1\u4e0d\u591f!", "\"\\u94b1\\u4e0d\\u591f!\"");
        }

        private static void ArrayTest<T>(T[] expected, string json, bool skipNewton = false)
        {
            var sw = new Stopwatch();
            sw.Start();
            T[] value1 = json.TinyJsonParse<T[]>();
            sw.Stop();
            CollectionAssert.AreEqual(expected, value1);
            var factor1 = WriteMetrics(sw, json, false);
            
            if (skipNewton)
                return;
            
            sw.Start();
            T[] value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<T[]>(json);
            sw.Stop();
            CollectionAssert.AreEqual(value2, value1);
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);
        }

        [TestMethod]
        public void TestArrayOfValues()
        {
            ArrayTest(new string[] { "one", "two", "three" }, "[\"one\",\"two\",\"three\"]");
            ArrayTest(new int[] { 1, 2, 3 }, "[1,2,3]");
            ArrayTest(new bool[] { true, false, true }, "     [true    ,    false,true     ]   ");
            ArrayTest(new object[] { null, null }, "[null,null]");
            ArrayTest(new float[] { 0.24f, 1.2f }, "[0.24,1.2]");
            ArrayTest(new double[] { 0.15, 0.19 }, "[0.15, 0.19]");
            ArrayTest<object>(null, "[garbled", true);
        }

        static void ListTest<T>(List<T> expected, string json, bool skipNewton = false)
        {
            var sw = new Stopwatch();
            sw.Start();
            var value1 = json.TinyJsonParse<List<T>>();
            sw.Stop();
            CollectionAssert.AreEqual(expected, value1);
            var factor1 = WriteMetrics(sw, json, false);
            
            if (skipNewton)
                return;
            
            sw.Start();
            var value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<List<T>>(json);
            sw.Stop();
            CollectionAssert.AreEqual(value2, value1);
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);
        }

        [TestMethod]
        public void TestListOfValues()
        {
            ListTest(new List<string> { "one", "two", "three" }, "[\"one\",\"two\",\"three\"]");
            ListTest(new List<int> { 1, 2, 3 }, "[1,2,3]");
            ListTest(new List<bool> { true, false, true }, "     [true    ,    false,true     ]   ");
            ListTest(new List<object> { null, null }, "[null,null]");
            ListTest(new List<float> { 0.24f, 1.2f }, "[0.24,1.2]");
            ListTest(new List<double> { 0.15, 0.19 }, "[0.15, 0.19]");
            ListTest<object>(null, "[garbled", true);
        }

        [TestMethod]
        public void TestRecursiveLists()
        {
            var expected = new List<List<int>> { new List<int> { 1, 2 }, new List<int> { 3, 4 } };
            var actual = "[[1,2],[3,4]]".TinyJsonParse<List<List<int>>>();
            Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
                CollectionAssert.AreEqual(expected[i], actual[i]);
        }

        [TestMethod]
        public void TestRecursiveArrays()
        {
            var expected = new int[][] { new int[] { 1, 2 }, new int[] { 3, 4 } };
            var actual = "[[1,2],[3,4]]".TinyJsonParse<int[][]>();
            Assert.AreEqual(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
                CollectionAssert.AreEqual(expected[i], actual[i]);
        }

        static void DictTest<K, V>(Dictionary<K, V> expected, string json, bool skipNewton = false)
        {
            var sw = new Stopwatch();
            sw.Start();
            var value1 = json.TinyJsonParse<Dictionary<K, V>>();
            sw.Stop();
            Assert.AreEqual(expected.Count, value1.Count);
            foreach (var pair in expected)
            {
                Assert.IsTrue(value1.ContainsKey(pair.Key));
                Assert.AreEqual(pair.Value, value1[pair.Key]);
            }
            var factor1 = WriteMetrics(sw, json, false);
            
            if (skipNewton)
                return;
            
            sw.Start();
            var value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<K, V>>(json);
            sw.Stop();
            Assert.AreEqual(expected.Count, value2.Count);
            foreach (var pair in expected)
            {
                Assert.IsTrue(value2.ContainsKey(pair.Key));
                Assert.AreEqual(pair.Value, value2[pair.Key]);
            }
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);
        }

        [TestMethod]
        public void TestDictionary()
        {
            DictTest(new Dictionary<string, int> { { "foo", 5 }, { "bar", 10 }, { "baz", 128 } }, "{\"foo\":5,\"bar\":10,\"baz\":128}");
            
            // This json string is technically valid json that Newtonsoft could handle, but it is not any kind of typical representation.
            DictTest(new Dictionary<int, int> { { 0, 5 } }, "{0:5}");
            
            // This json string one is very typical representation of Dictionary<int, int>
            DictTest(new Dictionary<int, int> { { 0, 5 } }, "{\"0\":5}");

            DictTest(new Dictionary<string, float> { { "foo", 5f }, { "bar", 10f }, { "baz", 128f } }, "{\"foo\":5,\"bar\":10,\"baz\":128}");
            DictTest(new Dictionary<string, string> { { "foo", "\"" }, { "bar", "hello" }, { "baz", "," } }, "{\"foo\":\"\\\"\",\"bar\":\"hello\",\"baz\":\",\"}");
            
            DictTest(new Dictionary<int, float> { { 1, 5f }, { 2, 10f }, { 3, 128f } }, "{\"1\":5,\"2\":10,\"3\":128}");
            DictTest(new Dictionary<ushort, float> { { 1, 5f }, { 2, 10f }, { 3, 128f } }, "{\"1\":5,\"2\":10,\"3\":128}");
            
            DictTest(new Dictionary<decimal, float> { { 1.0m, 5f }, { 2.2m, 10f }, { 3.3m, 128f } }, "{\"1.0\":5,\"2.2\":10,\"3.3\":128}");
            DictTest(new Dictionary<decimal, float> { { 1.0m, 5f }, { 2.2m, 10f }, { 3.3m, 128f } }, "{1.0:5,2.2:10,3.3:128}", true);
            
            DictTest(new Dictionary<Color, float> { { Color.Red, 5f }, { Color.Green, 10f }, { Color.Blue, 128f } }, "{\"0\":5,\"1\":10,\"2\":128}");
            
            DictTest(new Dictionary<TimeSpan, float> { { TimeSpan.Zero, 5f }, { TimeSpan.Parse("01:02:03"), 10f } }, "{\"00:00:00\":5,\"01:02:03\":10}");
        }

        [TestMethod]
        public void TestRecursiveDictionary()
        {
            var result = "{\"foo\":{ \"bar\":\"\\\"{,,:[]}\" }}".TinyJsonParse<Dictionary<string, Dictionary<string, string>>>();
            Assert.AreEqual("\"{,,:[]}", result["foo"]["bar"]);
        }

        class SimpleObject
        {
            public int A;
            public float B;
            public string C { get; set; }
            public List<int> D { get; set; }
        }

        [TestMethod]
        public void TestSimpleObject()
        {
            var json = "{\"A\":123,\"b\":456,\"C\":\"789\",\"D\":[10,11,12]}";
            var sw = new Stopwatch();
            sw.Start();
            SimpleObject value1 = json.TinyJsonParse<SimpleObject>();
            sw.Stop();
            Assert.IsNotNull(value1);
            Assert.AreEqual(123, value1.A);
            Assert.AreEqual(456f, value1.B);
            Assert.AreEqual("789", value1.C);
            CollectionAssert.AreEqual(new List<int> { 10, 11, 12 }, value1.D);
            var factor1 = WriteMetrics(sw, json, false);
            
            sw.Start();
            SimpleObject value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<SimpleObject>(json);
            sw.Stop();
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);

            value1 = "dfpoksdafoijsdfij".TinyJsonParse<SimpleObject>();
            Assert.IsNull(value1);
        }

        struct SimpleStruct
        {
            public int Id;
            public SimpleObject Obj;
        }

        [TestMethod]
        public void TestSimpleStruct()
        {
            var json = "{\"Id\":32,\"obj\":{\"A\":12345}}";
            var sw = new Stopwatch();
            sw.Start();
            SimpleStruct value1 = json.TinyJsonParse<SimpleStruct>();
            sw.Stop();
            Assert.IsNotNull(value1.Obj);
            Assert.AreEqual(value1.Obj.A, 12345);
            Assert.AreEqual(value1.Id, 32);
            var factor1 = WriteMetrics(sw, json, false);
            
            sw.Start();
            SimpleStruct value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<SimpleStruct>(json);
            sw.Stop();
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);
        }

        struct TinyStruct
        {
            public int Value;
        }

        [TestMethod]
        public void TestListOfStructs()
        {
            var values = "[{\"Value\":1},{\"Value\":2},{\"Value\":3}]".TinyJsonParse<List<TinyStruct>>();
            for (int i = 0; i < values.Count; i++)
                Assert.AreEqual(i + 1, values[i].Value);
        }

        class TestObject2
        {
            public TestObject2 A;
            public List<TestObject2> B;
            public SimpleStruct C;
        }

        [TestMethod]
        public void TestDeepObject()
        {
            var value = "{\"A\":{\"A\":{\"A\":{}}}}".TinyJsonParse<TestObject2>();
            Assert.IsNotNull(value);
            Assert.IsNotNull(value.A);
            Assert.IsNotNull(value.A.A);
            Assert.IsNotNull(value.A.A.A);

            value = "{\"B\":[{},null,{\"A\":{}}]}".TinyJsonParse<TestObject2>();
            Assert.IsNotNull(value);
            Assert.IsNotNull(value.B);
            Assert.IsNotNull(value.B[0]);
            Assert.IsNull(value.B[1]);
            Assert.IsNotNull(value.B[2].A);

            value = "{\"C\":{\"Obj\":{\"A\":5}}}".TinyJsonParse<TestObject2>();
            Assert.IsNotNull(value);
            Assert.IsNotNull(value.C.Obj);
            Assert.AreEqual(5, value.C.Obj.A);
        }

        class TestObject3
        {
            public int A, B, C, D, E, F;
            public TestObject3 Z { get; set; }
        }

        private static string GetPerformanceTestJson()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("[");
            const int numTests = 100000;
            for (int i = 0; i < numTests; i++)
            {
                builder.Append("{\"Z\":{\"F\":10}}");
                if (i < numTests - 1)
                    builder.Append(",");
            }
            builder.Append("]");
            return builder.ToString();
        }
        
        [TestMethod]
        public void PerformanceTest()
        {
            // Its not good form, but this test is actually verifying quite a several aspects of the overall system.
            
            var json = GetPerformanceTestJson();
            Console.WriteLine($"JSON.String.Length = {json.Length}");
            
            var sw = new Stopwatch();
            sw.Start();
            var result1 = json.TinyJsonParse<List<TestObject3>>();
            sw.Stop();
            for (int i = 0; i < result1.Count; i++)
                Assert.AreEqual(10, result1[i].Z.F);
            var factor1 = WriteMetrics(sw, json, false);

            var tinyJson = result1.TinyJsonTabConvert(false);
            var filePath = new FileInfo("TinyPerfIndented.json").FullName;
            result1.TinyJsonTabConvert(filePath, false);
            Assert.AreEqual(tinyJson.Length, File.ReadAllText(filePath).Length);
            
            var indentedResult = tinyJson.TinyJsonParse<List<TestObject3>>();
            Assert.IsNotNull(indentedResult); // trivial verification that the indented version is still valid json
            
            tinyJson = result1.TinyJsonConvert();
            filePath = new FileInfo("TinyPerf.json").FullName;
            result1.TinyJsonConvert(filePath);
            Assert.AreEqual(tinyJson.Length, File.ReadAllText(filePath).Length);
            Console.WriteLine($"TinyJson String.Length = {tinyJson.Length}");
            
            
            sw.Start();
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TestObject3>>(json);
            sw.Stop();
            var factor2 = WriteMetrics(sw, json, true);

            var newtJson = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
            filePath = new FileInfo("NewtPerfIndented.json").FullName;
            File.WriteAllText(filePath, newtJson);
            
            newtJson = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            filePath = new FileInfo("NewtPerf.json").FullName;
            File.WriteAllText(filePath, newtJson);
            Console.WriteLine($"NewtJson String.Length = {newtJson.Length}");

            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2); // TODO: See 2024/08/24 README.md notes on JSONParser
        }

        
        

        [TestMethod]
        public void CorruptionTest()
        {
            "{{{{{{[[[]]][[,,,,]],],],]]][[nulldsfoijsfd[[]]]]]]]]]}}}}}{{{{{{{{{D{FD{FD{F{{{{{}}}XXJJJI%&:,,,,,".TinyJsonParse<object>();
            "[[,[,,[,:::[[[[[[[".TinyJsonParse<List<List<int>>>();
            "{::,[][][],::::,}".TinyJsonParse<Dictionary<string, object>>();
        }

        [TestMethod]
        public void DynamicParserTest()
        {
            List<object> list = (List<object>)("[0,1,2,3]".TinyJsonParse<object>());
            Assert.IsTrue(list.Count == 4 && (int)list[3] == 3);
            Dictionary<string, object> obj = (Dictionary<string, object>)("{\"Foo\":\"Bar\"}".TinyJsonParse<object>());
            Assert.IsTrue((string)obj["Foo"] == "Bar");

            string testJson = "{\"A\":123,\"B\":456,\"C\":\"789\",\"D\":[10,11,12]}";
            Assert.AreEqual(testJson, ((Dictionary<string, object>)testJson.TinyJsonParse<object>()).TinyJsonConvert());
        }

        public struct NastyStruct
        {
            public byte R, G, B;
            public NastyStruct(byte r, byte g, byte b)
            {
                R = r; G = g; B = b;
            }
            public static NastyStruct Nasty = new NastyStruct(0, 0, 0);
        }

        [TestMethod]
        public void TestNastyStruct()
        {
            NastyStruct s = "{\"R\":234,\"G\":123,\"B\":11}".TinyJsonParse<NastyStruct>();
            Assert.AreEqual(234, s.R);
            Assert.AreEqual(123, s.G);
            Assert.AreEqual(11, s.B);
        }

        [TestMethod]
        public void TestEscaping()
        {
            var orig = new Dictionary<string, string> { { "hello", "world\n \" \\ \b \r \\0\u263A" } };
            var parsed = "{\"hello\":\"world\\n \\\" \\\\ \\b \\r \\0\\u263a\"}".TinyJsonParse<Dictionary<string, string>>();
            Assert.AreEqual(orig["hello"], parsed["hello"]);
        }

        [TestMethod]
        public void TestMultithread()
        {
            // Lots of threads
            for (int i = 0; i < 100; i++)
            {
                new Thread(() =>
                {
                    // Each threads has enough work to potentially hit a race condition
                    for (int j = 0; j < 10000; j++)
                    {
                        TestValues();
                        TestArrayOfValues();
                        TestListOfValues();
                        TestRecursiveLists();
                        TestRecursiveArrays();
                        TestDictionary();
                        TestRecursiveDictionary();
                        TestSimpleObject();
                        TestSimpleStruct();
                        TestListOfStructs();
                        TestDeepObject();
                        CorruptionTest();
                        DynamicParserTest();
                        TestNastyStruct();
                        TestEscaping();
                    }
                }).Start();
            }
        }

        class IgnoreDataMemberObject
        {
            public int A;
            [IgnoreDataMember]
            public int B;

            public int C { get; set; }
            [IgnoreDataMember]
            public int D { get; set; }
        }

        [TestMethod]
        public void TestIgnoreDataMember()
        {
            IgnoreDataMemberObject value = "{\"A\":123,\"B\":456,\"Ignored\":10,\"C\":789,\"D\":14}".TinyJsonParse<IgnoreDataMemberObject>();
            Assert.IsNotNull(value);
            Assert.AreEqual(123, value.A);
            Assert.AreEqual(0, value.B);
            Assert.AreEqual(789, value.C);
            Assert.AreEqual(0, value.D);
        }

        class DataMemberObject
        {
            [DataMember(Name = "a")]
            public int A;
            [DataMember()]
            public int B;

            [DataMember(Name = "c")]
            public int C { get; set; }
            public int D { get; set; }
        }

        [TestMethod]
        public void TestDataMemberObject()
        {
            DataMemberObject value = "{\"a\":123,\"B\":456,\"c\":789,\"D\":14}".TinyJsonParse<DataMemberObject>();
            Assert.IsNotNull(value);
            Assert.AreEqual(123, value.A);
            Assert.AreEqual(456, value.B);
            Assert.AreEqual(789, value.C);
            Assert.AreEqual(14, value.D);
        }

        public class EnumClass
        {
            public Color Colors;
            public Style Style;
        }

        [TestMethod]
        public void TestEnumMember()
        {
            EnumClass value = "{\"Colors\":\"Green\",\"Style\":\"Bold, Underline\"}".TinyJsonParse<EnumClass>();
            Assert.IsNotNull(value);
            Assert.AreEqual(Color.Green, value.Colors);
            Assert.AreEqual(Style.Bold | Style.Underline, value.Style);

            value = "{\"Colors\":3,\"Style\":10}".TinyJsonParse<EnumClass>();
            Assert.IsNotNull(value);
            Assert.AreEqual(Color.Yellow, value.Colors);
            Assert.AreEqual(Style.Italic | Style.Strikethrough, value.Style);

            value = "{\"Colors\":\"3\",\"Style\":\"10\"}".TinyJsonParse<EnumClass>();
            Assert.IsNotNull(value);
            Assert.AreEqual(Color.Yellow, value.Colors);
            Assert.AreEqual(Style.Italic | Style.Strikethrough, value.Style);

            value = "{\"Colors\":\"sfdoijsdfoij\",\"Style\":\"sfdoijsdfoij\"}".TinyJsonParse<EnumClass>();
            Assert.IsNotNull(value);
            Assert.AreEqual(Color.Red, value.Colors);
            Assert.AreEqual(Style.None, value.Style);
        }

        [TestMethod]
        public void TestDuplicateKeys()
        {
            var parsed = @"{""hello"": ""world"", ""goodbye"": ""heaven"", ""hello"": ""hell""}".TinyJsonParse<Dictionary<string, object>>();
            /*
             * We expect the parser to process the (valid) JSON above containing a duplicated key. The dictionary ensures that there is
             * only one entry with the duplicate key.
             */
            Assert.IsTrue(parsed.ContainsKey("hello"), "The dictionary is missing the duplicated key");
            /*
             * We also expect the other keys in the JSON to be processed as normal
             */
            Assert.IsTrue(parsed.ContainsKey("goodbye"), "The dictionary is missing the non-duplicated key");
            /*
             * The parser should store the last occurring value for the given key
             */
            Assert.AreEqual(parsed["hello"], "hell", "The parser stored an incorrect value for the duplicated key");
        }

        [TestMethod]
        public void TestDuplicateKeysInAnonymousObject()
        {
            var parsed = @"{""hello"": ""world"", ""goodbye"": ""heaven"", ""hello"": ""hell""}".TinyJsonParse<object>();
            var dictionary = (Dictionary<string, object>)parsed;
            /*
             * We expect the parser to process the (valid) JSON above containing a duplicated key. The dictionary ensures that there is
             * only one entry with the duplicate key.
             */
            Assert.IsTrue(dictionary.ContainsKey("hello"), "The dictionary is missing the duplicated key");
            /*
             * We also expect the other keys in the JSON to be processed as normal
             */
            Assert.IsTrue(dictionary.ContainsKey("goodbye"), "The dictionary is missing the non-duplicated key");
            /*
             * The parser should store the last occurring value for the given key
             */
            Assert.AreEqual(dictionary["hello"], "hell", "The parser stored an incorrect value for the duplicated key");
        }

        public abstract class GenericSimpleObject<T>
        {
            public abstract int Id { get; set; }
            public abstract T Obj { get; set; }
        }

        public class GenericComplexImp<TValue> : GenericSimpleObject<EnumClass>
        {
            public override int Id { get; set; }
            public TValue AltId { get; set; }
            public override EnumClass Obj { get; set; }
        }
        
        [TestMethod]
        public void TestSimpleGenericObject()
        {
            var json = "{\"Id\":32,\"AltId\":\"ALT23\",\"Obj\":{\"Colors\":\"Green\",\"Style\":\"Bold, Underline\"}}";
            var sw = new Stopwatch();
            sw.Start();
            var value1 = json.TinyJsonParse<GenericComplexImp<string>>();
            sw.Stop();
            Assert.IsNotNull(value1);
            Assert.IsNotNull(value1.Obj);
            Assert.AreEqual(Color.Green, value1.Obj.Colors);
            Assert.AreEqual(Style.Bold | Style.Underline, value1.Obj.Style);
            Assert.AreEqual(value1.Id, 32);
            Assert.AreEqual("ALT23", value1.AltId);
            var factor1 = WriteMetrics(sw, json, false);

            // TODO reorganize test model stubs so its easier to put this with the correct test group 
            var back = value1.TinyJsonConvert();
            Assert.AreEqual(json, back);
            
            sw.Start();
            var value2 = Newtonsoft.Json.JsonConvert.DeserializeObject<GenericComplexImp<string>>(json);
            sw.Stop();
            var factor2 = WriteMetrics(sw, json, true);
            
            // Expect performance at least as good as newtonsoft (only fair on single test execution)
            // Assert.IsTrue(factor1 <= factor2);
        }
        
    }
}
