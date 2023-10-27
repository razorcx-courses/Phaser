using Prism.Mvvm;

namespace RazorCX.Phaser.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private string _title = "RazorCX Phaser";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public MainWindowViewModel()
        {
	        new Models.Phaser().Process();
		}
    }
}
