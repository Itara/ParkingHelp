using System.Data;

namespace ParkingHelp.DB
{
    public enum DBType
    {
        Mssql,
        PostgreSql,
        MariaDB,
        Oracle
    }
    public interface iDBManager
    {
        public int Update();
        public int Delete();
        public int Insert();
        public DataTable Select();
    }
}
