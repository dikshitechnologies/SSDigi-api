using System.Globalization;

namespace JEWELLBISREACT.Global
{
    public class DateValidate
    {

        public static DateTime ConvertToSqlDateTime(string input)
        {
            // List of accepted formats
            string[] formats = {
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss", // ISO format
        "yyyy-MM-ddTHH:mm:ss.fff"
    };

            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            // fallback: Try general parse
            if (DateTime.TryParse(input, out result))
            {
                return result;
            }

            // If all parsing fails
            throw new FormatException("Invalid date format.");
        }

    }
}
