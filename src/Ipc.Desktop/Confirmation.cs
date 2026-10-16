using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ipc.Desktop;
public static class Confirmation
{
    public static bool Ask(Window owner, string message, string confirmLabel = "确认此操作", bool requireAcknowledgment = false, string cancelLabel = "取消")
    {
        var returnFocus = Keyboard.FocusedElement;
        var window = new Window { Owner=owner,Title="核对当前对象与后果",Width=640,MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-60),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false };
        var grid=new Grid { Margin=new Thickness(28) };
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto}); grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); grid.RowDefinitions.Add(new(){Height=GridLength.Auto}); grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        grid.Children.Add(new TextBlock {Text=confirmLabel,FontSize=24,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,16)});
        var scroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=Math.Max(180,SystemParameters.WorkArea.Height-340),Content=new TextBlock {Text=message,TextWrapping=TextWrapping.Wrap,FontSize=16}};
        Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        var check=new CheckBox {Content="已核对上述对象、版本、数量与实际处理后果",MinHeight=44,Margin=new Thickness(0,16,0,8),Visibility=requireAcknowledgment?Visibility.Visible:Visibility.Collapsed};
        Grid.SetRow(check,2);grid.Children.Add(check);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var cancel=new Button {Content=cancelLabel,IsCancel=true,MinWidth=146};
        var confirm=new Button {Content=confirmLabel,IsDefault=false,MinWidth=146,IsEnabled=!requireAcknowledgment,Style=(Style)owner.FindResource(confirmLabel=="确认强制结束"?"DangerButton":"Primary")};
        check.Checked+=(_,_)=>confirm.IsEnabled=true;check.Unchecked+=(_,_)=>confirm.IsEnabled=false;
        cancel.Click+=(_,_)=>window.DialogResult=false;confirm.Click+=(_,_)=>window.DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);Grid.SetRow(buttons,3);grid.Children.Add(buttons);
        KeyboardNavigation.SetTabNavigation(grid,KeyboardNavigationMode.Cycle);window.Content=grid;
        window.Loaded+=(_,_)=>cancel.Focus();
        window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter)e.Handled=true;};
        var result=window.ShowDialog()==true;
        if(returnFocus is UIElement {IsVisible:true,IsEnabled:true} input) Keyboard.Focus(input);
        return result;
    }
}
