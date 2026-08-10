namespace JEWELLBISREACT.DBConnection
{
    public static class DBHelper
    {
        public static string GetConnection()
        {
            string connection = @"Data Source=app.dikshitech.com;Initial Catalog=SSDIGI;User ID=sa;Password=Varsha@123#$;Trust Server Certificate=True";
            return connection; 
        }
    }
}
