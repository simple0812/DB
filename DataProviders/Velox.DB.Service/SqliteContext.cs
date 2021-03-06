using System;

namespace Velox.DB.Sqlite
{
    public class SqlServiceContext : Vx.Context
    {
        /*
        public static void Use(string dbFileName)
        {
            Vx.DB = new SqlServiceContext(dbFileName);
        }*/

        public SqlServiceContext() : base(new SqlServiceDataProvider())
        {
        }

        public SqlServiceContext(string server, string login, string password) : base(new SqlServiceDataProvider(server,login,password))
        {
        }
    }
}