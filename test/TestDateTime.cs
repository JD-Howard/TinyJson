using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TinyJson.Test;

[TestClass]
public class TestDateTime
{
    [TestMethod]
    public void SerializeDateTimeNow()
    {
        var now = DateTime.Now;
        var expected = $"\"{now:O}\"";
        var serialized = now.TinyJsonConvert();
            
        Assert.AreEqual(expected, serialized);
    }

    [TestMethod]
    public void SerializeDateTimeUtcNow()
    {
        var now = DateTime.UtcNow;
        var expected = $"\"{now:O}\"";
        var serialized = now.TinyJsonConvert();
            
        Assert.AreEqual(expected, serialized);
    }

    [TestMethod]
    public void DeserializeDateTimeNow()
    {
        var dto = DateTime.Now;
        var serialized = dto.TinyJsonConvert();
        var deSerialized = serialized.TinyJsonParse<DateTime>();

        Assert.AreEqual(dto, deSerialized);
    }

    [TestMethod]
    public void DeserializeDateTimeUtcNow()
    {
        var dto = DateTime.UtcNow;
        var serialized = dto.TinyJsonConvert();
        var deSerialized = serialized.TinyJsonParse<DateTime>();

        Assert.AreEqual(dto, deSerialized.ToUniversalTime());
    }

    [TestMethod]
    public void TestDateTimeFormats()
    {
        //Assert.AreEqual("2021".ToJson().FromJson<DateTime>(), new DateTime(2021,1,1,0,0,0));
        Assert.AreEqual("2021-06".TinyJsonConvert().TinyJsonParse<DateTime>(), new DateTime(2021,6,1));
        Assert.AreEqual("2021-06-19".TinyJsonConvert().TinyJsonParse<DateTime>(), new DateTime(2021,6,19));
        Assert.AreEqual("2021-02-16T14:07:24.3912313Z".TinyJsonConvert().TinyJsonParse<DateTime>(), new DateTime(2021, 2, 16, 14, 7, 24).AddTicks(3912313));

        Assert.AreEqual(new DateTime(2021, 6, 1).TinyJsonConvert().TinyJsonParse<DateTime>(), new DateTime(2021, 6, 1));
        Assert.AreEqual(new DateTime(2021, 6, 1, 8, 10, 30).TinyJsonConvert().TinyJsonParse<DateTime>(), new DateTime(2021, 6, 1, 8, 10, 30));
    }

    public class DateTimeTest : IEquatable<DateTimeTest>
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public bool Equals(DateTimeTest other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Start.Equals(other.Start) && End.Equals(other.End);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DateTimeTest) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }
    }

    [TestMethod]
    public void SerializeObjectWithDateTimes()
    {
        var defaultValues = "{\"Start\":\"0001-01-01T00:00:00.0000000\",\"End\":\"0001-01-01T00:00:00.0000000\"}";
        Assert.AreEqual(defaultValues, new DateTimeTest().TinyJsonConvert());

        var start = DateTime.Now;
        var onlyStart = $"{{\"Start\":\"{start:O}\",\"End\":\"0001-01-01T00:00:00.0000000\"}}";
        Assert.AreEqual(onlyStart, new DateTimeTest() { Start = start }.TinyJsonConvert());

        var end = DateTime.Now.AddHours(-1);
        var onlyEnd = $"{{\"Start\":\"0001-01-01T00:00:00.0000000\",\"End\":\"{end:O}\"}}";
        Assert.AreEqual(onlyEnd, new DateTimeTest() { End = end }.TinyJsonConvert());

        var bothValues = $"{{\"Start\":\"{start:O}\",\"End\":\"{end:O}\"}}";
        Assert.AreEqual(bothValues, new DateTimeTest() { Start = start, End = end }.TinyJsonConvert());
    }

    [TestMethod]
    public void DeserializeObjectWithDateTimes()
    {
        Assert.AreEqual(new DateTimeTest(), new DateTimeTest().TinyJsonConvert().TinyJsonParse<DateTimeTest>());

        var oneProp = new DateTimeTest() { Start = DateTime.Now };
        Assert.AreEqual(oneProp, oneProp.TinyJsonConvert().TinyJsonParse<DateTimeTest>());

        var twoProps = new DateTimeTest() { Start = DateTime.Now, End = DateTime.Now.AddHours(-1) };
        Assert.AreEqual(twoProps, twoProps.TinyJsonConvert().TinyJsonParse<DateTimeTest>());
    }

    public class NullableDateTimeTest : IEquatable<NullableDateTimeTest>
    {
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        public bool Equals(NullableDateTimeTest other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Nullable.Equals(Start, other.Start) && Nullable.Equals(End, other.End);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((NullableDateTimeTest) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }
    }

    [TestMethod]
    public void SerializeObjectWithNullableDateTimes()
    {
        var defaultValues = "{}";
        Assert.AreEqual(defaultValues, new NullableDateTimeTest().TinyJsonConvert());

        // var bothNulls = "{\"Start\":null,\"End\":null}";
        // Assert.AreEqual(bothNulls, new NullableDateTimeTest().ToJson());

        var start = DateTime.Now;
        var endIsNull = $"{{\"Start\":\"{start:O}\"}}";
        Assert.AreEqual(endIsNull, new NullableDateTimeTest() { Start = start }.TinyJsonConvert());

        var end = DateTime.Now.AddHours(-1);
        var startIsNull = $"{{\"End\":\"{end:O}\"}}";
        Assert.AreEqual(startIsNull, new NullableDateTimeTest() { End = end }.TinyJsonConvert());

        var noNulls = $"{{\"Start\":\"{start:O}\",\"End\":\"{end:O}\"}}";
        Assert.AreEqual(noNulls, new NullableDateTimeTest() { Start = start, End = end }.TinyJsonConvert());
    }

    [TestMethod]
    public void DeserializeObjectWithNullableDateTimes()
    {
        Assert.AreEqual(new NullableDateTimeTest(), new NullableDateTimeTest().TinyJsonConvert().TinyJsonParse<NullableDateTimeTest>());

        var oneNull = new NullableDateTimeTest() { Start = DateTime.Now };
        Assert.AreEqual(oneNull, oneNull.TinyJsonConvert().TinyJsonParse<NullableDateTimeTest>());

        var twoNulls = new NullableDateTimeTest() { Start = DateTime.Now, End = DateTime.Now.AddHours(-1) };
        Assert.AreEqual(twoNulls, twoNulls.TinyJsonConvert().TinyJsonParse<NullableDateTimeTest>());
    }

    [TestMethod]
    public void SerializeTimeSpan()
    {
        var timeSpan = TimeSpan.FromSeconds(10);
        var expected = $"\"{timeSpan}\"".TinyJsonParse<TimeSpan>().TinyJsonConvert();
        var serialized = timeSpan.TinyJsonConvert();

        Assert.AreEqual(expected, serialized);
    }

    [TestMethod]
    public void DeserializeTimeSpan()
    {
        var dto = TimeSpan.FromSeconds(10);
        var serialized = dto.TinyJsonConvert();
        var deSerialized = serialized.TinyJsonParse<TimeSpan>();

        Assert.AreEqual(dto, deSerialized);
    }

    public class TimeSpanTest : IEquatable<TimeSpanTest>
    {
        public TimeSpan? Duration { get; set; }

        public bool Equals(TimeSpanTest other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Nullable.Equals(Duration, other.Duration);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TimeSpanTest) obj);
        }

        public override int GetHashCode()
        {
            return Duration.GetHashCode();
        }
    }

    [TestMethod]
    public void SerializeNullableTimeSpan()
    {
        var nullValue = "{}";
        Assert.AreEqual(nullValue, new TimeSpanTest().TinyJsonConvert());

        var witValue = $"{{\"Duration\":\"00:00:00.0780782\"}}";
        Assert.AreEqual(witValue, new TimeSpanTest { Duration = TimeSpan.FromTicks(780782) }.TinyJsonConvert());
    }

    [TestMethod]
    public void DeserializeNullableTimeSpan()
    {
        Assert.AreEqual(new TimeSpanTest(), new TimeSpanTest().TinyJsonConvert().TinyJsonParse<TimeSpanTest>());

        var timeSpan = TimeSpan.FromSeconds(1);
        Assert.AreEqual(new TimeSpanTest() { Duration = timeSpan }, new TimeSpanTest { Duration = timeSpan }.TinyJsonConvert().TinyJsonParse<TimeSpanTest>());
    }
}