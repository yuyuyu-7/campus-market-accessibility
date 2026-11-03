using System.IO;
using System.Windows;
using Campus.Server;
namespace Campus.Desktop;
public static class Program {
 [STAThread] public static void Main(string[] args){
  var path=Option(args,"--db")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CampusMarketCourse","campus.db");var port=int.TryParse(Option(args,"--port"),out var n)?n:5078;
  if(args.Contains("--server")){var host=ApiHost.Start(path,port).GetAwaiter().GetResult();Thread.Sleep(Timeout.Infinite);return;}
  var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
  app.Startup+=async(_,_)=>{try{var host=await ApiHost.Start(path,port);app.Exit+=(_,_)=>Task.Run(async()=>{await host.StopAsync();await host.DisposeAsync();}).GetAwaiter().GetResult();var w=new MainWindow(new ApiClient(port));app.MainWindow=w;w.Show();}catch(Exception e){MessageBox.Show("启动失败。请确认没有重复运行程序，且本机端口未被占用。\n"+e.Message,"校园二手交易系统");app.Shutdown(1);}};
  app.Run();
 }
 static string? Option(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
}
