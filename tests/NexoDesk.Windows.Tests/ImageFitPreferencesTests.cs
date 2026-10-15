using NexoDesk.App.ViewModels;
using NexoDesk.Shared.Configuration;

namespace NexoDesk.Windows.Tests;

public class ImageFitPreferencesTests
{
    [Fact]
    public void PreviewIsImmediateButWritesAreCoalescedAndResetRestoresStableValues()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                int saves = 0, percent = -1; bool enabled = false;
                var vm = new ImageFitViewModel(new AdminSettings(), (e, p) => { enabled = e; percent = p; saves++; });
                int notifications = 0; vm.PropertyChanged += (_, _) => notifications++;
                vm.SharpnessPercent = 40; vm.SharpnessPercent = 70; vm.SharpnessPercent = 100;
                Assert.Equal(100, vm.SharpnessPercent); Assert.Equal(3, notifications); Assert.Equal(0, saves);
                vm.Flush(); Assert.Equal(1, saves); Assert.Equal(100, percent); Assert.True(enabled);
                vm.Flush(); Assert.Equal(1, saves);
                vm.EnhancedWindowFit = false; vm.SharpnessPercent = 0;
                vm.Flush(); Assert.False(enabled); Assert.Equal(0, percent);
                vm.ResetCommand.Execute(null); vm.Flush(); Assert.True(enabled); Assert.Equal(35, percent);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
