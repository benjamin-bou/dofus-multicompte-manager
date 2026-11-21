using System.Windows;

namespace DofusMulticompte.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>« Voir le journal » en bas de la page Personnages.</summary>
    private void ShowJournal(object sender, RoutedEventArgs e) => Tabs.SelectedItem = JournalTab;
}
