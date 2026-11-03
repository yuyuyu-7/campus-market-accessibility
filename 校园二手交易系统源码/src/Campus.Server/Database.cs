using Campus.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace Campus.Server;

// 每个实体独立表；所有仓储共用本次连接和事务。服务不持有 SQL。
public sealed class Database {
 readonly string connectionString; public readonly object Gate=new();
 public Database(string path) {
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  connectionString=new SqliteConnectionStringBuilder{DataSource=path,DefaultTimeout=5}.ToString();
  using var c=Open();using var cmd=c.CreateCommand();
  cmd.CommandText="PRAGMA journal_mode=WAL;";cmd.ExecuteNonQuery();
  foreach(var t in new[]{typeof(User),typeof(Product),typeof(Trade),typeof(Picture),typeof(Session),typeof(ReviewJob),typeof(Dedup),typeof(LoginGuard)}) {
   cmd.CommandText=$"CREATE TABLE IF NOT EXISTS [{t.Name}](id TEXT PRIMARY KEY, body TEXT NOT NULL CHECK(json_valid(body)))";cmd.ExecuteNonQuery();
  }
  cmd.CommandText="CREATE UNIQUE INDEX IF NOT EXISTS account_unique ON [User](json_extract(body,'$.Account'))";cmd.ExecuteNonQuery();
  cmd.CommandText="CREATE TABLE IF NOT EXISTS Probe(id TEXT PRIMARY KEY, value TEXT NOT NULL)";cmd.ExecuteNonQuery();
 }
 public SqliteConnection Open(){var c=new SqliteConnection(connectionString);c.Open();return c;}
 public bool Ready(){lock(Gate){using var c=Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT OR REPLACE INTO Probe VALUES('probe','ok'); SELECT value FROM Probe WHERE id='probe'";var ok=(string?)cmd.ExecuteScalar()=="ok";tx.Rollback();return ok;}}
 public T Run<T>(Func<IEntityStore,T> f) {lock(Gate){using var c=Open();using var tx=c.BeginTransaction();var result=f(new Store(c,tx));tx.Commit();return result;}}
 sealed class Store(SqliteConnection c,SqliteTransaction tx):IEntityStore {
  SqliteCommand Cmd<T>(string sql,string? id=null,string? body=null){var x=c.CreateCommand();x.Transaction=tx;x.CommandText=sql.Replace("{table}","["+typeof(T).Name+"]");if(id!=null)x.Parameters.AddWithValue("$id",id);if(body!=null)x.Parameters.AddWithValue("$body",body);return x;}
  public T? Get<T>(string id){using var x=Cmd<T>("SELECT body FROM {table} WHERE id=$id",id);return x.ExecuteScalar() is string s?JsonSerializer.Deserialize<T>(s):default;}
  public List<T> All<T>(){using var x=Cmd<T>("SELECT body FROM {table}");using var r=x.ExecuteReader();var list=new List<T>();while(r.Read())list.Add(JsonSerializer.Deserialize<T>(r.GetString(0))!);return list;}
  public void Put<T>(string id,T value){using var x=Cmd<T>("INSERT INTO {table}(id,body) VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body",id,JsonSerializer.Serialize(value));x.ExecuteNonQuery();}
  public void Remove<T>(string id){using var x=Cmd<T>("DELETE FROM {table} WHERE id=$id",id);x.ExecuteNonQuery();}
 }
}
