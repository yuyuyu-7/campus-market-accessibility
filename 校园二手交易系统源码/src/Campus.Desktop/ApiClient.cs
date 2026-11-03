using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
namespace Campus.Desktop;
public sealed class ApiClient {
 readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(15)};readonly string clientId=Guid.NewGuid().ToString("N");
 public string Token="";public JsonObject? User;public string BaseUrl {get;}
 public ApiClient(int port){BaseUrl=$"http://127.0.0.1:{port}";}
 public async Task<JsonNode> Call(string method,string path,object? body=null){
  using var req=new HttpRequestMessage(new HttpMethod(method),BaseUrl+path);if(Token!="")req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Token);
  if(method!="GET"){req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));req.Headers.Add("X-Client-Id",clientId);req.Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(body??new{}),Encoding.UTF8,"application/json");}
  using var response=await http.SendAsync(req);var result=JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
  if(!response.IsSuccessStatusCode)throw new Exception(result["message"]?.ToString()+"（"+result["code"]+"）");return result["data"]??new JsonObject();
 }
}
