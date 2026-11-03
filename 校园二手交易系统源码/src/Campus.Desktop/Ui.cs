using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
namespace Campus.Desktop;
// 集中放置视图工厂；业务逻辑位于服务模块。
public static class Ui {
 public static TextBlock Text(string s,double size=16)=>new(){Text=s,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};
 public static Button Button(string text,Action action){var b=new Button{Content=text,Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,4,8,4),MinHeight=36};Name(b,System.Text.RegularExpressions.Regex.Replace(text,@"\(_?.\)",""));b.Click+=(_,_)=>action();return b;}
 public static void Name(DependencyObject control,string name,string help=""){AutomationProperties.SetName(control,name);AutomationProperties.SetAutomationId(control,name);if(help!="")AutomationProperties.SetHelpText(control,help);}
 public static void Field(Panel panel,string label,Control input,string help=""){var caption=Text(label);panel.Children.Add(caption);AutomationProperties.SetLabeledBy(input,caption);Name(input,label,help);input.Margin=new Thickness(0,0,0,10);input.MinHeight=34;panel.Children.Add(input);if(help!="")panel.Children.Add(Text(help,13));}
 public static void Theme(Window w){w.FontFamily=new FontFamily("Microsoft YaHei UI");w.FontSize=16;w.SetResourceReference(Control.BackgroundProperty,SystemColors.WindowBrushKey);w.SetResourceReference(Control.ForegroundProperty,SystemColors.WindowTextBrushKey);KeyboardNavigation.SetTabNavigation(w,KeyboardNavigationMode.Cycle);
  // 动态系统色会随系统高对比度主题变化；不硬编码白底黑字。
  foreach(var type in new[]{typeof(TextBox),typeof(PasswordBox),typeof(ListBox)}){var style=new Style(type);style.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(SystemColors.WindowBrushKey)));style.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(SystemColors.WindowTextBrushKey)));w.Resources[type]=style;}
  var visual=new FrameworkElementFactory(typeof(Border));visual.SetValue(Border.BorderThicknessProperty,new Thickness(2));visual.SetResourceReference(Border.BorderBrushProperty,SystemColors.WindowTextBrushKey);visual.SetValue(Border.MarginProperty,new Thickness(-3));var focus=new Style(typeof(Control));focus.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(Control)){VisualTree=visual}));w.Resources[SystemParameters.FocusVisualStyleKey]=focus;
 }
 public static void Announce(TextBlock status,string message){AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Assertive);AutomationProperties.SetName(status,"操作结果："+message);AutomationProperties.SetAutomationId(status,"operation-result");status.Text=message;status.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{var peer=UIElementAutomationPeer.FromElement(status)??UIElementAutomationPeer.CreatePeerForElement(status);peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);}));}
 public static void FocusFirst(FrameworkElement root) {root.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));}));}
}
public sealed class FormWindow:Window {
 public readonly StackPanel Fields=new();public readonly TextBlock Status=Ui.Text("");public readonly StackPanel Actions=new(){Orientation=Orientation.Horizontal};
 public FormWindow(Window owner,string title){Owner=owner;Title=title;Width=620;Height=680;MinWidth=480;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;Ui.Theme(this);var root=new DockPanel{Margin=new Thickness(24)};var footer=new StackPanel();footer.Children.Add(Status);footer.Children.Add(Actions);DockPanel.SetDock(footer,Dock.Bottom);KeyboardNavigation.SetTabNavigation(footer,KeyboardNavigationMode.Local);KeyboardNavigation.SetTabIndex(footer,1);root.Children.Add(footer);var scroll=new ScrollViewer{Content=Fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false};KeyboardNavigation.SetTabNavigation(scroll,KeyboardNavigationMode.Local);KeyboardNavigation.SetTabIndex(scroll,0);root.Children.Add(scroll);Content=root;var close=Ui.Button("关闭(_C)",Close);close.IsCancel=true;Actions.Children.Add(close);Loaded+=(_,_)=>Ui.FocusFirst(this);}
 public void Submit(string name,Func<Task> action){var b=Ui.Button(name,()=>{});b.IsDefault=true;b.Click+=async(_,_)=>{if(!b.IsEnabled)return;b.IsEnabled=false;try{await action();}catch(Exception e){Ui.Announce(Status,e.Message);}finally{b.IsEnabled=true;}};Actions.Children.Insert(0,b);}
 public void ShowOwned(){var previous=Keyboard.FocusedElement;ShowDialog();if(previous is UIElement element&&element.IsVisible&&element.IsEnabled)element.Focus();}
}
