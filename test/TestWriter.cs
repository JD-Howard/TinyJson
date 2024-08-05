using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TinyJson;

namespace TinyJson.Test
{
    [TestClass]
    public class TestWriter
    {
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

        [TestMethod]
        public void TestValues()
        {
            Assert.AreEqual("\"\u94b1\u4e0d\u591f!\"", "\u94b1\u4e0d\u591f!".TinyJsonConvert());
            Assert.AreEqual("123", 123.TinyJsonConvert());
            Assert.AreEqual("true", true.TinyJsonConvert());
            Assert.AreEqual("false", false.TinyJsonConvert());
            Assert.AreEqual("[1,2,3]", new int[] { 1, 2, 3 }.TinyJsonConvert());
            Assert.AreEqual("[1,2,3]", new List<int> { 1, 2, 3 }.TinyJsonConvert());
            Assert.AreEqual("\"Green\"", Color.Green.TinyJsonConvert());
            Assert.AreEqual("\"Green\"", ((Color)1).TinyJsonConvert());
            Assert.AreEqual("\"10\"", ((Color)10).TinyJsonConvert());
            Assert.AreEqual("\"Bold\"", Style.Bold.TinyJsonConvert());
            Assert.AreEqual("\"Bold, Italic\"", (Style.Bold | Style.Italic).TinyJsonConvert());
            Assert.AreEqual("\"19\"", (Style.Bold | Style.Italic | (Style)16).TinyJsonConvert());
        }

        [TestMethod]
        public void TestDicts()
        {
            Assert.AreEqual("{\"foo\":\"bar\"}", new Dictionary<string, string> { { "foo", "bar" } }.TinyJsonConvert());
            Assert.AreEqual("{\"foo\":123}", new Dictionary<string, int> { { "foo", 123 } }.TinyJsonConvert());
            
            Assert.AreEqual("{\"1\":5,\"2\":10,\"3\":128}", new Dictionary<int, float> { { 1, 5f }, { 2, 10f }, { 3, 128f } }.TinyJsonConvert());
            Assert.AreEqual("{\"1.0\":5,\"2.2\":10,\"3.3\":128}", new Dictionary<decimal, float> { { 1.0m, 5f }, { 2.2m, 10f }, { 3.3m, 128f } }.TinyJsonConvert());
            Assert.AreEqual("{\"Red\":5,\"Green\":10}", new Dictionary<Color, float> { { Color.Red, 5f }, { Color.Green, 10f } }.TinyJsonConvert());
            Assert.AreEqual("{\"00:00:00\":5,\"01:02:03\":10}", new Dictionary<TimeSpan, float> { { TimeSpan.Zero, 5f }, { TimeSpan.Parse("01:02:03"), 10f } }.TinyJsonConvert());
        }

        class SimpleObject
        {
            public SimpleObject A;
            public List<int> B;
            public string C { get; set; }

            // Should not serialize
            private int D = 333;
            public static int E = 555;
            internal int F = 777;
            protected int G = 999;
            public const int H = 111;
        }

        struct SimpleStruct
        {
            public SimpleObject A;
        }

        class InheritedObject : SimpleObject
        {
            public int X;
        }

        [TestMethod]
        public void TestObjects()
        {
            Assert.AreEqual("{\"A\":{},\"B\":[1,2,3],\"C\":\"Test\"}", new SimpleObject { A = new SimpleObject(), B = new List<int> { 1, 2, 3 }, C = "Test" }.TinyJsonConvert());
            Assert.AreEqual("{\"A\":{\"A\":{},\"B\":[1,2,3],\"C\":\"Test\"}}", new SimpleStruct { A = new SimpleObject { A = new SimpleObject(), B = new List<int> { 1, 2, 3 }, C = "Test" } }.TinyJsonConvert());
            Assert.AreEqual("{\"X\":9,\"A\":{},\"B\":[1,2,3],\"C\":\"Test\"}", new InheritedObject { A = new SimpleObject(), B = new List<int> { 1, 2, 3 }, C = "Test", X = 9 }.TinyJsonConvert());
        }

        public struct NastyStruct
        {
            public int R, G, B;
            public NastyStruct(byte r, byte g, byte b)
            {
                R = r; G = g; B = b;
            }
            public static NastyStruct Nasty = new NastyStruct(0, 0, 0);
        }

        [TestMethod]
        public void TestNastyStruct()
        {
            Assert.AreEqual("{\"R\":1,\"G\":2,\"B\":3}", new NastyStruct(1,2,3).TinyJsonConvert());
        }

        [TestMethod]
        public void TestEscaping()
        {
            Assert.AreEqual("{\"hello\":\"world\\n \\\\ \\\" \\b \\r \\u0000\u263A\"}", new Dictionary<string,string>{
                {"hello", "world\n \\ \" \b \r \0\u263A"}
            }.TinyJsonConvert());
        }

        class IgnoreDataMemberObject
        {
            [IgnoreDataMember]
            public int A;
            public int B;
            [IgnoreDataMember]
            public int C { get; set; }
            public int D { get; set; }
        }

        [TestMethod]
        public void TestIgnoreDataMemberObject()
        {
            Assert.AreEqual("{\"B\":20,\"D\":40}", new IgnoreDataMemberObject { A = 10, B = 20, C = 30, D = 40 }.TinyJsonConvert());
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
            Assert.AreEqual("{\"a\":10,\"B\":20,\"c\":30,\"D\":40}", new DataMemberObject { A = 10, B = 20, C = 30, D = 40 }.TinyJsonConvert());
        }

        public class EnumClass
        {
            public Color Colors;
            public Style Style;
        }

        [TestMethod]
        public void TestEnumMember()
        {
            Assert.AreEqual("{\"Colors\":\"Green\",\"Style\":\"Bold\"}", new EnumClass { Colors = Color.Green, Style = Style.Bold }.TinyJsonConvert());
            Assert.AreEqual("{\"Colors\":\"Green\",\"Style\":\"Bold, Underline\"}", new EnumClass { Colors = Color.Green, Style = Style.Bold | Style.Underline }.TinyJsonConvert());
            Assert.AreEqual("{\"Colors\":\"Blue\",\"Style\":\"Italic, Underline\"}", new EnumClass { Colors = (Color)2, Style = (Style)6 }.TinyJsonConvert());
            Assert.AreEqual("{\"Colors\":\"Blue\",\"Style\":\"Underline\"}", new EnumClass { Colors = (Color)2, Style = (Style)4 }.TinyJsonConvert());
            Assert.AreEqual("{\"Colors\":\"10\",\"Style\":\"17\"}", new EnumClass { Colors = (Color)10, Style = (Style)17 }.TinyJsonConvert());
        }

        public class PrimitiveObject
        {
            public bool Bool;
            public byte Byte;
            public sbyte SByte;
            public short Short;
            public ushort UShort;
            public int Int;
            public uint UInt;
            public long Long;
            public ulong ULong;
            public char Char;
            public float Single;
            public double Double;
            public decimal Decimal;
        }

        [TestMethod]
        public void TestPrimitives()
        {
            Assert.AreEqual(
                "{\"Bool\":true,\"Byte\":17,\"SByte\":-17,\"Short\":-123,\"UShort\":123,\"Int\":-56,\"UInt\":56,\"Long\":-34,\"ULong\":34,\"Char\":\"C\",\"Single\":4.3,\"Double\":5.6,\"Decimal\":10.1}",
                new PrimitiveObject
                {
                    Bool = true,
                    Byte = 17,
                    SByte = -17,
                    Short = -123,
                    UShort = 123,
                    Int = -56,
                    UInt = 56,
                    Long = -34,
                    ULong = 34,
                    Char = 'C',
                    Single = 4.3f,
                    Double = 5.6,
                    Decimal = 10.1M
                }.TinyJsonConvert());
        }


        public class SimpleClassNullables
        {
            public int Id { get; set; }
            public bool? A { get; set; }
            public EnumClass B { get; set; }
        }
        
        [TestMethod]
        public void VerifyIncludeNullBehaviorFlag() // This indirectly verifies a nullable primitive too.
        {
            var stub = new SimpleClassNullables() {Id = 5};
            var json = string.Empty;
            
            Assert.AreEqual("{\"Id\":5}", json = stub.TinyJsonConvert());
            Assert.AreEqual("{\"Id\":5,\"A\":null,\"B\":null}", json = stub.TinyJsonConvert(true));

            stub.A = true;
            Assert.AreEqual("{\"Id\":5,\"A\":true}", json = stub.TinyJsonConvert());
            Assert.AreEqual("{\"Id\":5,\"A\":true,\"B\":null}", json = stub.TinyJsonConvert(true));

            stub.B = new EnumClass();
            json = stub.TinyJsonConvert();
            Assert.AreEqual("{\"Id\":5,\"A\":true,\"B\":{\"Colors\":\"Red\",\"Style\":\"None\"}}", json);
            Assert.AreEqual(json, stub.TinyJsonConvert(true));
        }
        
        
        [TestMethod]
        public void VerifyTabBehavior() // This indirectly verifies a nullable primitive too.
        {
            var stub = new SimpleClassNullables() {Id = 5};
            var json = string.Empty;
            
            Assert.AreEqual("{\n\t\"Id\":5,\n\t\"A\":null,\n\t\"B\":null\n}", json = stub.TinyJsonTabConvert(true));
            Assert.AreEqual("{\n\t\"Id\":5\n}", json = stub.TinyJsonTabConvert(false));

            stub.B = new EnumClass();
            Assert.AreEqual("{\n\t\"Id\":5,\n\t\"B\":{\n\t\t\"Colors\":\"Red\",\n\t\t\"Style\":\"None\"\n\t}\n}", json = stub.TinyJsonTabConvert(false));
        }

        
        // TODO do performance test TinyJsonConvert vs Newtonsoft. I suspect indented formatting will be the only drastic difference 
    }
}
