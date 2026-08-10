using JEWELLBISREACT.DBConnection;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Global
{
    public class CheckIfValueExists
    {


        public async Task<bool> DoesValueExist(string tableName, string columnName, string valueToCheck)
        {
            var allowedTables = new HashSet<string> { "ITEMTRANSACTION", "ITEMPURCHASE", "ILEDGER", "BLEDGER", "LEDGER", "STONEDET" };
            if (!allowedTables.Contains(tableName.ToUpper()))
            {
                throw new ArgumentException("Invalid table name.");
            }

            // Create dynamic query based on passed table and column name
            string query = $"SELECT COUNT(1) FROM {tableName} WHERE {columnName} = @ValueToCheck";

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@ValueToCheck", valueToCheck);
                        int count = (int)await cmd.ExecuteScalarAsync();
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error checking value existence.", ex);
            }
        }

    }
}
