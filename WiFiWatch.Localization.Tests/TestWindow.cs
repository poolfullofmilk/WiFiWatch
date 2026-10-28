// A Window Boundary For Headless Rendering; No WPF Or Native UI Is Started
namespace System.Windows
{
    public sealed class DependencyPropertyChangedEventArgs : EventArgs;
}

namespace WiFiWatch.Desktop
{
    public sealed class MainWindow
    {
        public bool IsVisible => true;
        public event Action<
            object,
            System.Windows.DependencyPropertyChangedEventArgs
        >? IsVisibleChanged
        {
            add { }
            remove { }
        }
        public event Func<Task>? CloseRequested
        {
            add { }
            remove { }
        }

        public void Hide() { }

        public void Exit() { }
    }
}
