using Campus.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Campus.Server;

public static class ApiHost {
 public static async Task<WebApplication> Start(string dbPath,int port=5078) {
  var db=new Database(dbPath);var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseSetting("urls",$"http://127.0.0.1:{port}");builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o=>o.Limits.MaxRequestBodySize=3145728);var app=builder.Build();
  app.Run(async ctx=>{
   var requestId=Rules.Id();int status=200;object? data=null;string code="OK",message="成功";
   try{
    Rules.Need(!ctx.Request.Headers.ContainsKey("Origin"),"不接受浏览器跨域请求。","FORBIDDEN",403);
    var path=ctx.Request.Path.Value!.TrimEnd('/');var method=ctx.Request.Method;var token=ctx.Request.Headers.Authorization.ToString().Replace("Bearer ","");
    bool isPublic=(method=="GET"&&(path=="/health"||path=="/ready"||path.StartsWith("/api/v1/products")||path.StartsWith("/api/v1/images/")))||(method=="POST"&&(path=="/api/v1/users"||path=="/api/v1/sessions"));
    var user=db.Run(s=>new UserService(s).Authenticate(token));Rules.Need(isPublic||user!=null,"请先登录，或重新登录已过期的会话。","AUTH_REQUIRED",401);
    if(path=="/ready"&&method=="GET")Rules.Need(db.Ready(),"数据库探测未通过。","NOT_READY",503);
    JsonObject body=new();string raw="";bool write=method!="GET";
    if(write){Rules.Need(ctx.Request.ContentLength is null||ctx.Request.ContentLength<=3145728,"请求体超过3 MiB。","BODY_TOO_LARGE",413);Rules.Need(ctx.Request.ContentType?.StartsWith("application/json")==true,"请求必须使用JSON。","CONTENT_TYPE",415);using var reader=new StreamReader(ctx.Request.Body);raw=await reader.ReadToEndAsync();Rules.Need(System.Text.Encoding.UTF8.GetByteCount(raw)<=3145728,"请求过大。","BODY_TOO_LARGE",413);try{body=JsonNode.Parse(raw) as JsonObject??throw new JsonException();}catch(JsonException){throw new RuleError(422,"JSON_INVALID","JSON格式不正确。");}}
    var key=ctx.Request.Headers["Idempotency-Key"].ToString();var client=ctx.Request.Headers["X-Client-Id"].ToString();if(write){Rules.Need(key.Length is >=8 and <=128,"写请求需要8至128位幂等键。");if(user==null)Rules.Need(client.Length is >=8 and <=128,"匿名写请求需要客户端标识。");}
    data=db.Run<object?>(s=>{
     var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();string dedupId=UserService.Digest((user?.Id??client)+method+path+key);var hash=UserService.Digest(raw);
     foreach(var old in s.All<Dedup>().Where(x=>now-x.Created>=30))s.Remove<Dedup>(old.Id);
     if(write){var old=s.Get<Dedup>(dedupId);if(old!=null)throw new RuleError(409,old.Hash==hash?"DUPLICATE_SUBMIT":"IDEMPOTENCY_CONFLICT",old.Hash==hash?"30秒内重复提交已拦截，请查询首次操作结果。":"同一提交标识对应了不同内容，请刷新后重试。");}
     var users=new UserService(s);var products=new ProductService(s);var trades=new TradeService(s,products);var images=new ImageService(s);object? result;
     var parts=path.Split('/',StringSplitOptions.RemoveEmptyEntries);string id=parts.Length>3?parts[3]:"";string action=parts.Length>4?parts[4]:"";
     if(path=="/health"&&method=="GET")result=new{status="healthy",application="CampusMarket",version="1.1"};
     else if(path=="/ready"&&method=="GET")result=new{ready=true,database="read_write_ok"};
     else if(path=="/api/v1/users"&&method=="POST"){result=users.Register(body);status=201;}
     else if(path=="/api/v1/sessions"&&method=="POST")result=users.Login(body,ctx.Connection.RemoteIpAddress?.ToString()??"local");
     else if(path=="/api/v1/session"&&method=="DELETE"){Fields.Only(body);s.Remove<Session>(UserService.Digest(token));result=new{loggedOut=true,revoked=true};}
     else if(path=="/api/v1/me"&&method=="GET")result=users.Public(user!);
     else if(path=="/api/v1/me"&&method=="PATCH")result=users.Update(user!,body);
     else if(path=="/api/v1/images"&&method=="POST"){result=images.Upload(user!,body);status=201;}
     else if(path.StartsWith("/api/v1/images/")&&parts.Length==4&&method=="GET")result=images.Read(id,user);
     else if(path=="/api/v1/products"&&method=="POST"){result=products.Write(user!,body);status=201;}
     else if((path=="/api/v1/products"||path=="/api/v1/me/products")&&method=="GET"){
      var list=s.All<Product>().Where(p=>path.Contains("/me/")?p.SellerId==user!.Id:p.Status=="在售");var q=ctx.Request.Query["keyword"].ToString();var cat=ctx.Request.Query["category"].ToString();Rules.Need(q.Length<=100&&cat.Length<=30,"搜索条件过长。","QUERY_INVALID");if(q!="")list=list.Where(x=>x.Title.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Description.Contains(q,StringComparison.OrdinalIgnoreCase));if(cat!="")list=list.Where(x=>x.Category==cat);result=Page(list.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Select(products.Public),ctx);
     }
     else if(path.StartsWith("/api/v1/products/")&&parts.Length==4&&method=="GET"){var p=products.Get(id);Rules.Need(p.Status=="在售"||p.SellerId==user?.Id,"商品当前未公开。","NOT_FOUND",404);result=products.Public(p);}
     else if(path.StartsWith("/api/v1/products/")&&parts.Length==4&&method=="PATCH")result=products.Write(user!,body,id);
     else if(path.StartsWith("/api/v1/products/")&&action=="withdraw"&&parts.Length==5&&method=="POST")result=products.Withdraw(user!,id,body);
     else if(path=="/api/v1/trades"&&method=="POST"){result=trades.Apply(user!,body);status=201;}
     else if(path=="/api/v1/trades"&&method=="GET"){var role=ctx.Request.Query["role"].ToString();Rules.Need(new[]{"","buyer","seller"}.Contains(role),"交易角色无效。","QUERY_INVALID");var list=s.All<Trade>().Where(t=>role=="buyer"?t.BuyerId==user!.Id:role=="seller"?t.SellerId==user!.Id:t.BuyerId==user!.Id||t.SellerId==user!.Id);result=Page(list.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Select(x=>trades.Public(x,user!)),ctx);}
     else if(path.StartsWith("/api/v1/trades/")&&parts.Length==4&&method=="GET")result=trades.Public(trades.Get(id),user!);
     else if(path.StartsWith("/api/v1/trades/")&&parts.Length==5&&new[]{"accept","reject","cancel","complete"}.Contains(action)&&method=="POST")result=trades.Act(user!,id,action,body);
     else throw new RuleError(404,"NOT_FOUND","接口不存在。");
     if(write&&result is not RuleError)s.Put(dedupId,new Dedup(dedupId,hash,now));return result;
    });
    if(data is RuleError error)throw error;
   }catch(RuleError e){status=e.Status;code=e.Code;message=e.Message;data=null;}
   catch(Microsoft.AspNetCore.Http.BadHttpRequestException){status=413;code="BODY_TOO_LARGE";message="请求体超过限制。";data=null;}
   catch(Microsoft.Data.Sqlite.SqliteException){status=503;code="STORAGE_BUSY";message="数据库暂不可用，请稍后重试。";data=null;}
   catch(Exception e){status=500;code="INTERNAL_ERROR";message="系统暂时无法处理请求。";data=null;System.Diagnostics.Trace.WriteLine(e);}
   ctx.Response.StatusCode=status;await ctx.Response.WriteAsJsonAsync(new{code,message,data,requestId,errors=Array.Empty<object>()});
  });
  var cancel=new CancellationTokenSource();app.Lifetime.ApplicationStopping.Register(cancel.Cancel);
  _=Task.Run(async()=>{while(!cancel.IsCancellationRequested){try{db.Run(s=>{new ProductService(s).Review(new WordReviewer());return true;});await Task.Delay(500,cancel.Token);}catch(OperationCanceledException){break;}catch{await Task.Delay(1000);}}});
  await app.StartAsync();return app;
 }
 static object Page(IEnumerable<object> values,HttpContext ctx){int page=1,size=20;var q=ctx.Request.Query;if(q.ContainsKey("page"))Rules.Need(int.TryParse(q["page"],out page)&&page>0,"页码无效。","QUERY_INVALID");if(q.ContainsKey("pageSize"))Rules.Need(int.TryParse(q["pageSize"],out size)&&size>0&&size<=50,"每页数量应在1至50之间。","QUERY_INVALID");var all=values.ToArray();return new{items=all.Skip((int)Math.Min((long)(page-1)*size,int.MaxValue)).Take(size).ToArray(),page,pageSize=size,total=all.Length};}
}
