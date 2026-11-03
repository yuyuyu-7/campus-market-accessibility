namespace Campus.Core;

public record User(string Id, string Account, string PasswordHash, string Nickname, string Contact, int Version=1);
public record Product(string Id, string SellerId, string Title, string Category, string Description, int PriceCents, string[] ImageIds, string Status, int Version, string CreatedAt, string ReviewReason="");
public record Trade(string Id, string ProductId, string BuyerId, string SellerId, string TitleSnapshot, int PriceSnapshot, string Status, int Version, string CreatedAt);
public record Picture(string Id, string OwnerId, string FileName, string MimeType, string ContentBase64);
public record Session(string Id, string UserId, long LastSeen);
public record ReviewJob(string Id, string ProductId, int Version);
public record Dedup(string Id, string Hash, long Created);
public record LoginGuard(string Id, int Count, long Since, long LockedUntil);
public interface IEntityStore { T? Get<T>(string id); List<T> All<T>(); void Put<T>(string id,T value); void Remove<T>(string id); }
public interface IProductForTrade { Product RequireForTrade(string id,int version); void Save(Product product); }
public interface ITextReviewer { string? Check(string text); }
public class RuleError(int status,string code,string message) : Exception(message) { public int Status {get;}=status; public string Code {get;}=code; }
public static class Rules {
 public static void Need(bool ok,string message,string code="VALIDATION_ERROR",int status=422) {if(!ok)throw new RuleError(status,code,message);}
 public static void Owner(bool ok) => Need(ok,"无权操作此记录。","FORBIDDEN",403);
 public static void Version(int expected,int actual) => Need(expected==actual,"记录已更新，请刷新后重试。","VERSION_CONFLICT",409);
 public static void State(bool ok) => Need(ok,"当前状态不允许此操作，请刷新查看。","STATE_CONFLICT",409);
 public static string Id() => Guid.NewGuid().ToString("N");
 public static string Now() => DateTimeOffset.UtcNow.ToString("O");
}
