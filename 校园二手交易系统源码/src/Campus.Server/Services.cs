using Campus.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
namespace Campus.Server;

public static class Fields {
 public static void Only(JsonObject o,params string[] names){Rules.Need(o.All(x=>names.Contains(x.Key)),"存在不允许的字段。");}
 public static string Text(JsonObject o,string k,int max=200){var v=o[k];Rules.Need(v is JsonValue j&&j.TryGetValue<string>(out _),k+" 必须是文字。");var s=v!.GetValue<string>().Trim();Rules.Need(s.Length>0&&s.Length<=max,k+" 必填且长度不能超过 "+max+"。");return s;}
 public static int Int(JsonObject o,string k,int min=1,int max=int.MaxValue){Rules.Need(o[k] is JsonValue j&&j.TryGetValue<int>(out var n)&&n>=min&&n<=max,k+" 必须是范围内的整数。");return o[k]!.GetValue<int>();}
}
public sealed class UserService(IEntityStore db) {
 public static string Digest(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
 static string Hash(string password){var salt=RandomNumberGenerator.GetBytes(16);return Convert.ToBase64String(salt)+":"+Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password,salt,100000,HashAlgorithmName.SHA256,32));}
 static bool Check(string p,string stored){var s=stored.Split(':');return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(s[1]),Rfc2898DeriveBytes.Pbkdf2(p,Convert.FromBase64String(s[0]),100000,HashAlgorithmName.SHA256,32));}
 public object Public(User u)=>new{u.Id,u.Account,u.Nickname,u.Contact,u.Version};
 public object Register(JsonObject o){Fields.Only(o,"account","password","nickname","contact");var a=Fields.Text(o,"account",32).ToLowerInvariant();Rules.Need(System.Text.RegularExpressions.Regex.IsMatch(a,"^[a-z0-9_]{3,32}$"),"账号需为3至32位字母、数字或下划线。");var p=Fields.Text(o,"password",128);Rules.Need(p.Length>=8,"密码至少8位。");var n=Fields.Text(o,"nickname",40);var contact=Fields.Text(o,"contact",100);Rules.Need(!db.All<User>().Any(x=>x.Account==a),"账号已存在。","ACCOUNT_EXISTS",409);var u=new User(Rules.Id(),a,Hash(p),n,contact);db.Put(u.Id,u);return new{userId=u.Id,u.Id,u.Account,u.Nickname,u.Contact,u.Version};}
 public object Login(JsonObject o,string source){Fields.Only(o,"account","password");var a=Fields.Text(o,"account",32).ToLowerInvariant();var p=Fields.Text(o,"password",128);var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var keys=new[]{"account:"+a,"source:"+source};foreach(var k in keys){var g=db.Get<LoginGuard>(k);Rules.Need(g==null||now>=g.LockedUntil,"失败次数过多，请5分钟后再试。","LOGIN_RATE_LIMIT",429);}var u=db.All<User>().FirstOrDefault(x=>x.Account==a);
 if(u==null||!Check(p,u.PasswordHash)){foreach(var k in keys){var g=db.Get<LoginGuard>(k);var count=g!=null&&now-g.Since<300?g.Count+1:1;db.Put(k,new LoginGuard(k,count,count==1?now:g!.Since,count>=5?now+300:0));}return new RuleError(401,"LOGIN_FAILED","账号或密码错误。");}
 db.Remove<LoginGuard>(keys[0]);var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var id=Digest(token);db.Put(id,new Session(id,u.Id,now));return new{token,expiresIn=1800,user=Public(u)};}
 public User? Authenticate(string token){if(string.IsNullOrEmpty(token))return null;var id=Digest(token);var s=db.Get<Session>(id);var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(s==null||now-s.LastSeen>=1800)return null;db.Put(id,s with{LastSeen=now});return db.Get<User>(s.UserId);}
 public object Update(User u,JsonObject o){Fields.Only(o,"nickname","contact","version");u=db.Get<User>(u.Id)!;Rules.Version(Fields.Int(o,"version"),u.Version);u=u with{Nickname=Fields.Text(o,"nickname",40),Contact=Fields.Text(o,"contact",100),Version=u.Version+1};db.Put(u.Id,u);return Public(u);}
}
// ai辅助完成ImageService.Upload 图片容量、格式一致性和实际解码校验
public sealed class ImageService(IEntityStore db) {
 public object Upload(User u,JsonObject o){Fields.Only(o,"fileName","mimeType","contentBase64");var name=Fields.Text(o,"fileName",200);var mime=Fields.Text(o,"mimeType",40);var encoded=Fields.Text(o,"contentBase64",2800000);byte[] bytes;
 try{Rules.Need(!encoded.Any(char.IsWhiteSpace),"图片编码不能包含空白。");bytes=Convert.FromBase64String(encoded);}catch(FormatException){throw new RuleError(422,"IMAGE_FORMAT","图片编码无效。");}
 Rules.Need(bytes.Length<=2097152,"图片不能超过2 MiB。","IMAGE_TOO_LARGE");var ext=Path.GetExtension(name).ToLowerInvariant();Rules.Need((mime=="image/png"&&ext==".png")||(mime=="image/jpeg"&&(ext==".jpg"||ext==".jpeg")),"仅支持格式一致的PNG或JPEG图片。","IMAGE_FORMAT");
 try{using var stream=new MemoryStream(bytes);var d=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);Rules.Need((mime=="image/png"&&d is PngBitmapDecoder)||(mime=="image/jpeg"&&d is JpegBitmapDecoder),"扩展名与实际图片格式不一致。","IMAGE_FORMAT");Rules.Need(d.Frames.Count==1&&d.Frames[0].PixelWidth<=4096&&d.Frames[0].PixelHeight<=4096,"图片宽高不得超过4096，且只支持单帧。","IMAGE_FORMAT");}
 catch(RuleError){throw;}catch{throw new RuleError(422,"IMAGE_FORMAT","图片损坏或无法解码。");}
 var image=new Picture(Rules.Id(),u.Id,Path.GetFileName(name),mime,encoded);db.Put(image.Id,image);return new{imageId=image.Id,sizeBytes=bytes.Length,mimeType=mime};}
 public object Read(string id,User? u){var p=db.Get<Picture>(id)??throw new RuleError(404,"NOT_FOUND","图片不存在。");Rules.Owner(p.OwnerId==u?.Id||db.All<Product>().Any(x=>x.Status=="在售"&&x.ImageIds.Contains(id)));return new{imageId=p.Id,p.MimeType,p.ContentBase64};}
}
public sealed class WordReviewer:ITextReviewer {public string? Check(string text)=>new[]{"违禁品","枪支","毒品"}.FirstOrDefault(text.Contains);}
// ai辅助完成ProductService.Review 异步审核任务与版本状态判断
public sealed class ProductService(IEntityStore db):IProductForTrade {
 public Product Get(string id)=>db.Get<Product>(id)??throw new RuleError(404,"NOT_FOUND","商品不存在。");
 public object Public(Product p)=>new{p.Id,p.Title,p.Category,p.Description,p.PriceCents,p.ImageIds,p.Status,p.Version,p.CreatedAt,sellerNickname=db.Get<User>(p.SellerId)?.Nickname,p.ReviewReason};
 public Product RequireForTrade(string id,int version){var p=Get(id);Rules.Version(version,p.Version);Rules.State(p.Status=="在售");return p;}
 public void Save(Product p)=>db.Put(p.Id,p);
 public object Write(User u,JsonObject o,string? id=null){if(id!=null)Rules.Owner(Get(id).SellerId==u.Id);Fields.Only(o,id==null?new[]{"title","category","description","priceCents","imageIds"}:new[]{"title","category","description","priceCents","imageIds","version"});var title=Fields.Text(o,"title",80);var category=Fields.Text(o,"category",30);var description=Fields.Text(o,"description",1000);var price=Fields.Int(o,"priceCents",1,1000000);Rules.Need(o["imageIds"] is JsonArray a&&a.Count>=1&&a.Count<=3,"需要1至3张图片。");var images=o["imageIds"]!.AsArray().Select(x=>x is JsonValue j&&j.TryGetValue<string>(out var s)?s:"").ToArray();Rules.Need(images.Distinct().Count()==images.Length&&images.All(x=>db.Get<Picture>(x)?.OwnerId==u.Id),"图片不存在、重复或不属于当前用户。");
 Product p;if(id==null)p=new(Rules.Id(),u.Id,title,category,description,price,images,"待审核",1,Rules.Now());else{p=Get(id);Rules.Owner(p.SellerId==u.Id);Rules.Version(Fields.Int(o,"version"),p.Version);Rules.State(new[]{"在售","待审核","审核驳回","已下架"}.Contains(p.Status)&&!db.All<Trade>().Any(x=>x.ProductId==id&&x.Status=="待处理"));p=p with{Title=title,Category=category,Description=description,PriceCents=price,ImageIds=images,Status="待审核",Version=p.Version+1,ReviewReason=""};}
 Save(p);var job=new ReviewJob(Rules.Id(),p.Id,p.Version);db.Put(job.Id,job);return Public(p);}
 public object Withdraw(User u,string id,JsonObject o){Fields.Only(o,"version");var p=Get(id);Rules.Owner(p.SellerId==u.Id);Rules.Version(Fields.Int(o,"version"),p.Version);Rules.State(new[]{"在售","待审核","审核驳回"}.Contains(p.Status));p=p with{Status="已下架",Version=p.Version+1};Save(p);foreach(var t in db.All<Trade>().Where(t=>t.ProductId==id&&t.Status=="待处理"))db.Put(t.Id,t with{Status="已拒绝",Version=t.Version+1});return Public(p);}
 public void Review(ITextReviewer reviewer){foreach(var j in db.All<ReviewJob>()){var p=db.Get<Product>(j.ProductId);if(p!=null&&p.Version==j.Version&&p.Status=="待审核"){var word=reviewer.Check(p.Title+" "+p.Description);Save(p with{Status=word==null?"在售":"审核驳回",ReviewReason=word==null?"":"包含不允许的词语，请修改后重提。",Version=p.Version+1});}db.Remove<ReviewJob>(j.Id);}}
}
// ai辅助完成TradeService.Act 交易角色、版本和状态转换逻辑。
public sealed class TradeService(IEntityStore db,IProductForTrade products) {
 public Trade Get(string id)=>db.Get<Trade>(id)??throw new RuleError(404,"NOT_FOUND","交易不存在。");
 public object Public(Trade t,User u){Rules.Owner(t.BuyerId==u.Id||t.SellerId==u.Id);var p=db.Get<Product>(t.ProductId)!;return new{t.Id,t.ProductId,t.TitleSnapshot,t.PriceSnapshot,t.Status,t.Version,t.CreatedAt,productVersion=p.Version,productStatus=p.Status,role=t.BuyerId==u.Id?"buyer":"seller",counterpartyContact=new[]{"已接受","已完成"}.Contains(t.Status)?db.Get<User>(t.BuyerId==u.Id?t.SellerId:t.BuyerId)?.Contact:null};}
 public object Apply(User u,JsonObject o){Fields.Only(o,"productId","productVersion");var p=products.RequireForTrade(Fields.Text(o,"productId"),Fields.Int(o,"productVersion"));Rules.Owner(p.SellerId!=u.Id);Rules.State(!db.All<Trade>().Any(x=>x.ProductId==p.Id&&x.BuyerId==u.Id&&new[]{"待处理","已接受"}.Contains(x.Status)));var t=new Trade(Rules.Id(),p.Id,u.Id,p.SellerId,p.Title,p.PriceCents,"待处理",1,Rules.Now());db.Put(t.Id,t);return Public(t,u);}
 public object Act(User u,string id,string action,JsonObject o){Fields.Only(o,action=="cancel"?new[]{"version"}:new[]{"version","productVersion"});var t=Get(id);Rules.Owner((action=="accept"||action=="reject")?t.SellerId==u.Id:t.BuyerId==u.Id);Rules.Version(Fields.Int(o,"version"),t.Version);var p=db.Get<Product>(t.ProductId)!;if(action!="cancel")Rules.Version(Fields.Int(o,"productVersion"),p.Version);
 if(action=="complete"){Rules.State(t.Status=="已接受"&&p.Status=="交易中");t=t with{Status="已完成",Version=t.Version+1};products.Save(p with{Status="已售出",Version=p.Version+1});}
 else{Rules.State(t.Status=="待处理");if(action=="accept"){Rules.State(p.Status=="在售");products.Save(p with{Status="交易中",Version=p.Version+1});foreach(var other in db.All<Trade>().Where(x=>x.ProductId==p.Id&&x.Id!=t.Id&&x.Status=="待处理"))db.Put(other.Id,other with{Status="已拒绝",Version=other.Version+1});}t=t with{Status=action=="accept"?"已接受":action=="reject"?"已拒绝":"已取消",Version=t.Version+1};}
 db.Put(t.Id,t);return Public(t,u);}
}
