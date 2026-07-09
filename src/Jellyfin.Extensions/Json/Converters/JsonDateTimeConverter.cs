using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MulletaFlix.Extensions.Json.Converters
{
    /// <summary>
    /// Legacy DateTime converter.
    /// Milliseconds aren't output if zero by default.
    /// Normalizes out-of-range dates (year &lt; 1900 or &gt; 2100) to a safe sentinel
    /// to prevent client SDKs (e.g. Kotlin's ZonedDateTime) from crashing on overflow.
    /// </summary>
    public class JsonDateTimeConverter : JsonConverter<DateTime>
    {
        /// <inheritdoc />
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetDateTime();
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            value = Normalize(value);

            if (value.Millisecond == 0)
            {
                // Remaining ticks value will be 0, manually format.
                writer.WriteStringValue(value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffZ", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }

        private static DateTime Normalize(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc && value.Year < 1900)
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            if (value.Kind == DateTimeKind.Local && value.Year < 1900)
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
            }

            if (value.Year < 1900 || value.Year > 2100)
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            return value;
        }
    }
}

