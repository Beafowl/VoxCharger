using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class LoadingForm : Form
    {
        private Action<LoadingForm> _action;
        private bool _completed = false;

        public LoadingForm()
        {
            InitializeComponent();
        }

        private void OnLoadingForm(object sender, EventArgs e)
        {
        }

        private void OnLoadingFormShown(object sender, EventArgs e)
        {
            // Run the action on a background thread so the dialog's message
            // pump stays responsive for SetStatus/SetProgress callbacks. The
            // asphyxia importer uses Task.WaitAll / Parallel.ForEach internally;
            // running it on the UI thread would deadlock as soon as a worker
            // tried to Invoke back to update the progress bar.
            Task.Run(() =>
            {
                try
                {
                    _action(this);
                }
                catch
                {
                    // Action implementations are expected to handle their own
                    // failures and call Complete() in a finally. The catch
                    // here is a backstop — without it, an unhandled exception
                    // would leave the dialog open forever.
                    Complete();
                    throw;
                }
            });
        }

        public void SetAction(Action<LoadingForm> action)
        {
            this._action = action;
        }

        public void SetStatus(string text)
        {
            if (StatusLabel.InvokeRequired)
            {
                StatusLabel.Invoke(new Action(() => SetStatus(text)));
                return;
            }

            StatusLabel.Text = text;
            StatusLabel.Update();
        }

        public void SetProgress(float percentage)
        {
            if (ProgressBar.InvokeRequired)
            {
                ProgressBar.Invoke(new Action<float>(SetProgress), percentage);
                return;
            }

            percentage = percentage > 100f ? 100f : percentage < 0f ? 0f : percentage;
            ProgressBar.Value = (int)percentage;
            ProgressBar.Update();
        }

        public void Complete()
        {
            _completed = true;
            if (InvokeRequired)
                Invoke(new Action(Close));
            else
                Close();
        }

        private void OnLoadingFormFormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !_completed;
        }
    }
}
