using Campus.Desktop;
using Campus.Server;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;

// 独立测试启动器：使用真实服务创建虚构账号，再构造真实MainWindow。
// 正式程序不引用本工程，不提供登录绕过入口。
public static class Harness {
 [STAThread] public static void Main(string[] args){
  var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var app=new Application();
  EventManager.RegisterClassHandler(typeof(Window),Keyboard.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((sender,e)=>{if(e.NewFocus is UIElement el){var peer=UIElementAutomationPeer.CreatePeerForElement(el);File.AppendAllText(Path.Combine(output,"keyboard-focus.log"),DateTimeOffset.Now.ToString("O")+" | "+(sender as Window)?.Title+" | "+el.GetType().Name+" | "+peer?.GetName()+Environment.NewLine);}}));
  InputManager.Current.PostProcessInput+=(_,e)=>{if(e.StagingItem.Input is KeyEventArgs key&&key.RoutedEvent==Keyboard.KeyUpEvent)File.AppendAllText(Path.Combine(output,"keyboard-input.log"),DateTimeOffset.Now.ToString("O")+" | "+key.Key+" | "+(Keyboard.FocusedElement as FrameworkElement)?.GetType().Name+" | "+AutomationProperties.GetName(Keyboard.FocusedElement as DependencyObject??app.MainWindow)+Environment.NewLine);};
  SystemParameters.StaticPropertyChanged+=(_,e)=>app.Dispatcher.BeginInvoke(new Action(()=>{if(e.PropertyName=="HighContrast")foreach(Window w in app.Windows)Capture(w,output,SystemParameters.HighContrast?"高对比度-"+w.Title:"普通主题-"+w.Title);}));
  app.Startup+=async(_,_)=>{try{
   var host=await ApiHost.Start(Path.Combine(output,"ui-test.db"),5081);app.Exit+=(_,_)=>Task.Run(async()=>{await host.StopAsync();await host.DisposeAsync();}).GetAwaiter().GetResult();
   var api=new ApiClient(5081);string account="ui_"+Guid.NewGuid().ToString("N")[..8];await api.Call("POST","/api/v1/users",new{account,password="Course123!",nickname="界面测试卖家",contact="demo@example.test"});
   var login=await api.Call("POST","/api/v1/sessions",new{account,password="Course123!"});api.Token=login["token"]!.ToString();api.User=login["user"]!.AsObject();
   var bitmap=BitmapSource.Create(8,8,96,96,PixelFormats.Rgb24,null,Enumerable.Repeat((byte)160,8*8*3).ToArray(),24);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using var ms=new MemoryStream();enc.Save(ms);
   var pic=await api.Call("POST","/api/v1/images",new{fileName="sample.png",mimeType="image/png",contentBase64=Convert.ToBase64String(ms.ToArray())});
   await api.Call("POST","/api/v1/products",new{title="界面测试教材",category="教材",description="封面完整，带少量笔记，校内当面交接。",priceCents=1800,imageIds=new[]{pic["imageId"]!.ToString()}});await Task.Delay(700);
   var w=new MainWindow(api);app.MainWindow=w;
   w.ContentRendered+=(_,_)=>{var who=typeof(MainWindow).GetField("who",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w) as TextBlock;who!.Text="当前用户：界面测试卖家（虚构测试数据）";Capture(w,output,"商品大厅");};
   // 各真实业务弹窗首次显示后记录其UIA名称/角色/状态和真实渲染图。
   EventManager.RegisterClassHandler(typeof(Window),Window.LoadedEvent,new RoutedEventHandler((sender,e)=>{if(sender is FormWindow form)form.Dispatcher.BeginInvoke(new Action(()=>Capture(form,output,form.Title)));}));
   w.Show();
  }catch(Exception e){File.WriteAllText(Path.Combine(output,"harness-error.txt"),e.ToString());app.Shutdown(1);}};app.Run();
 }
 static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in Walk(VisualTreeHelper.GetChild(root,i)))yield return x;}
 static void Capture(Window w,string output,string name){
  File.WriteAllText(Path.Combine(output,name+"-theme.json"),System.Text.Json.JsonSerializer.Serialize(new{highContrast=SystemParameters.HighContrast,window=SystemColors.WindowColor.ToString(),text=SystemColors.WindowTextColor.ToString(),highlight=SystemColors.HighlightColor.ToString(),background=w.Background?.ToString(),foreground=w.Foreground?.ToString()}));
  w.UpdateLayout();var rows=Walk(w).OfType<UIElement>().Select(el=>{var peer=UIElementAutomationPeer.CreatePeerForElement(el);if(peer==null)return null;return new{type=el.GetType().Name,name=peer.GetName(),role=peer.GetAutomationControlType().ToString(),enabled=peer.IsEnabled(),focusable=el.Focusable,password=peer.IsPassword(),help=peer.GetHelpText(),live=AutomationProperties.GetLiveSetting(el).ToString(),id=AutomationProperties.GetAutomationId(el)};}).Where(x=>x!=null).ToArray();
  File.WriteAllText(Path.Combine(output,name+"-UIA.json"),System.Text.Json.JsonSerializer.Serialize(rows,new System.Text.Json.JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  var v=(Visual)w.Content;var fe=(FrameworkElement)w.Content;var r=new RenderTargetBitmap((int)Math.Ceiling(fe.ActualWidth),(int)Math.Ceiling(fe.ActualHeight),96,96,PixelFormats.Pbgra32);r.Render(v);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(r));using var f=File.Create(Path.Combine(output,name+".png"));encoder.Save(f);
 }
}
