using System.IO;
using RedisLib;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Redis
{
    public partial class MainWindow : Window
    {
        Redis<string> Redis;
        CancellationTokenSource CancellationToken;
        Thread ThreadSerialize;
        readonly string FolderSave = Directory.GetCurrentDirectory() + "\\RedisData\\";

        public MainWindow()
        {
            InitializeComponent();
            if(!Directory.Exists(FolderSave))
                Directory.CreateDirectory(FolderSave);
            if (isEnablePersistence.IsChecked == true && Directory.GetFiles(FolderSave).Length > 0)
            {
                Redis = Redis<string>.Deserialize(File.ReadAllText(Directory.GetFiles(FolderSave).Max()!));
            }
            else
            {
                Redis = new Redis<string>(1000);
            }

            CancellationToken = new CancellationTokenSource();
            ThreadSerialize = new Thread(() =>
            {
                DateTime now = DateTime.Now;
                while (!CancellationToken.IsCancellationRequested)
                {
                    if(now < DateTime.Now.AddMinutes(1))
                    {
                        now = now.AddMinutes(1);
                        File.WriteAllText(
                            FolderSave + String.Format("Redis-{0}-{1}-{2}-{3}-{4}", 
                            DateTime.Now.Month, 
                            DateTime.Now.Day, 
                            DateTime.Now.Hour, 
                            DateTime.Now.Minute, 
                            DateTime.Now.Second ) + 
                            ".txt", Redis.Serialize());
                    }
                }
            });
            ThreadSerialize.Start();
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox box)
            {
                if (string.IsNullOrEmpty(box.Text))
                    box.Background = new SolidColorBrush(Color.FromArgb(0x60, 0xff, 0xff, 0xff));
                else
                    box.Background = new SolidColorBrush(Color.FromArgb(0xff, 0xff, 0xff, 0xff));
            }
        }

        private void Add(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!String.IsNullOrEmpty(Key.Text) && !String.IsNullOrEmpty(Value.Text))
                {
                    if (!String.IsNullOrEmpty(exp.Text) && int.TryParse(exp.Text, out int time) && time > 0)
                    {
                        Redis.Set(Key.Text.ToString(), Value.Text.ToString(), time);
                        AddResult.Content = String.Format("key: {0}, value: {1}, exp: {2} is added\t{3}", Key.Text, Value.Text, time.ToString(), DateTime.Now);
                    }
                    else if (String.IsNullOrEmpty(exp.Text))
                    {
                        Redis.Set(Key.Text.ToString(), Value.Text.ToString());
                        AddResult.Content = String.Format("key: {0}, value: {1} is added\t{2}", Key.Text, Value.Text, DateTime.Now);
                    }
                    else
                    {
                        AddResult.Content = String.Format("exp: {0} is not correct value\t{1}", exp.Text, DateTime.Now);
                    }
                }
                else
                {
                    AddResult.Content = "key or value is null\t" + DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                AddResult.Content = String.Format("Exception: {0}\t{1}", ex.Message, DateTime.Now);
            }
        }

        private void Get(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!String.IsNullOrEmpty(Key.Text))
                {
                    GetResult.Content = Redis.Get(Key.Text);
                }
                else
                {
                    GetResult.Content = "key is null\t" + DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                GetResult.Content = String.Format("Exception: {0}\t{1}", ex.Message, DateTime.Now);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            CancellationToken.Cancel();
            ThreadSerialize.Join();
            CancellationToken.Dispose();
            Redis.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}