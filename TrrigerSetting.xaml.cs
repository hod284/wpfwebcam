using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace wpfCCTV
{
    /// <summary>
    /// TrrigerSetting.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class TrrigerSetting : Window
    {
        private readonly string[] _commonClasses = new[]
     {
            "person", "car", "truck", "bus", "motorcycle", "bicycle",
            "cat", "dog", "bird", "horse",
            "traffic light", "stop sign", "fire hydrant", "parking meter",
            "bottle", "cup", "bowl", "chair", "couch", "bed",
            "laptop", "cell phone", "keyboard", "mouse", "tv",
            "backpack", "handbag", "suitcase", "umbrella"
        };

        public bool IsEnabled { get; private set; }
        public HashSet<string> SelectedClasses { get; private set; }
        public string SavePath { get; private set; }

        public TrrigerSetting()
        {
            InitializeComponent();
            IniteSelectList();
            IsEnabled = false;
            SelectedClasses = new HashSet<string>();
            SavePath = "";
        }
        private void IniteSelectList()
        {
            foreach (var className in _commonClasses)
            {
                var checkBox = new CheckBox
                {
                    Content = className,
                    Margin = new Thickness(5),
                    FontSize = 14
                };
                checkBox.Checked += ClassCheckBox_Changed;
                checkBox.Unchecked += ClassCheckBox_Changed;
                ClassListPanel.Children.Add(checkBox);
            }

        }
        private void ClassCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectedClasses();
        }
        private void UpdateSelectedClasses()
        {
            int Count = ClassListPanel.Children.OfType<CheckBox>().Count(x => x.IsChecked == true);
            SelectionInfoText.Text = $"Selected Classes: {Count}";
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "트리거 저장 폴더 선택",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SavePathTextBox.Text = dialog.SelectedPath;
                SavePath = dialog.SelectedPath;
            }
        }
        private void EnableButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SavePath))
            { 
                MessageBox.Show("저장 경로를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selectedClassesbox = ClassListPanel.Children.OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .ToList();
            if (selectedClassesbox.Count == 0)
            {
                MessageBox.Show("하나 이상의 클래스를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!Directory.Exists(SavePath))
            {
                try
                { 
                    Directory.CreateDirectory(SavePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"저장 경로를 생성할 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            //선택된 클래스 저장
            SelectedClasses = new HashSet<string>(selectedClassesbox.Select(cb => cb.Content.ToString()));
            IsEnabled = true;
            DialogResult = true;
            Close();
        }
        private void DisableButton_Click(object sender, RoutedEventArgs e)
        {
            IsEnabled = false;
            SelectedClasses = new HashSet<string>();
            DialogResult = true;
            Close();


        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
 