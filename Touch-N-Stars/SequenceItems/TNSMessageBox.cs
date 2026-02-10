using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.MyMessageBox;
using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TouchNStars.SequenceItems {

    [ExportMetadata("Name", "TNS Message Box")]
    [ExportMetadata("Description", "Display a message box that can be closed remotely via TNS API")]
    [ExportMetadata("Icon", "MessageBoxSVG")]
    [ExportMetadata("Category", "Touch 'N' Stars")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TNSMessageBox : SequenceItem {
        private IWindowServiceFactory windowServiceFactory;
        private Guid? currentMessageBoxId;
        private static bool templatesLoaded = false;
        private static readonly object templateLock = new object();

        // Static constructor to register the DataTemplates
        static TNSMessageBox() {
            lock (templateLock) {
                if (!templatesLoaded) {
                    try {
                        // Load the MessageBox item template (for Sequencer UI)
                        try {
                            var itemTemplate = new ResourceDictionary {
                                Source = new Uri("pack://application:,,,/TouchNStars;component/SequenceItems/Templates/TNSMessageBoxTemplate.xaml", UriKind.Absolute)
                            };
                            Application.Current?.Resources.MergedDictionaries.Add(itemTemplate);
                        } catch (System.UriFormatException itemEx) {
                            Logger.Warning($"Failed to load TNSMessageBox item template: {itemEx.Message}");
                        }

                        // Load the MessageBox result template (for Dialog window)
                        try {
                            var resultTemplate = new ResourceDictionary {
                                Source = new Uri("pack://application:,,,/TouchNStars;component/SequenceItems/Templates/TNSMessageBoxResultTemplate.xaml", UriKind.Absolute)
                            };
                            Application.Current?.Resources.MergedDictionaries.Add(resultTemplate);
                        } catch (System.UriFormatException resultEx) {
                            Logger.Warning($"Failed to load TNSMessageBox result template: {resultEx.Message}");
                        }

                        templatesLoaded = true;
                        Logger.Debug("TNSMessageBox templates loaded successfully");
                    } catch (Exception ex) {
                        Logger.Error($"Failed to load TNSMessageBox templates: {ex}");
                    }
                }
            }
        }

        [ImportingConstructor]
        public TNSMessageBox(IWindowServiceFactory windowServiceFactory) {
            this.windowServiceFactory = windowServiceFactory;
        }

        private TNSMessageBox(TNSMessageBox cloneMe) : this(cloneMe.windowServiceFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new TNSMessageBox(this) {
                Text = Text,
                CloseOnTimeout = CloseOnTimeout,
                TimeoutSeconds = TimeoutSeconds,
                ContinueOnTimeout = ContinueOnTimeout
            };
        }

        private string text = "Message from Touch 'N' Stars Sequence";
        [JsonProperty]
        public string Text {
            get => text;
            set {
                text = value;
                RaisePropertyChanged();
            }
        }

        private bool closeOnTimeout = false;
        [JsonProperty]
        public bool CloseOnTimeout {
            get => closeOnTimeout;
            set {
                closeOnTimeout = value;
                RaisePropertyChanged();
            }
        }

        private int timeoutSeconds = 60;
        [JsonProperty]
        public int TimeoutSeconds {
            get => timeoutSeconds;
            set {
                timeoutSeconds = value;
                RaisePropertyChanged();
            }
        }

        private bool continueOnTimeout = true;
        [JsonProperty]
        public bool ContinueOnTimeout {
            get => continueOnTimeout;
            set {
                continueOnTimeout = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var service = windowServiceFactory.Create();
            var msgBoxResult = new TNSMessageBoxResult(Text);
            currentMessageBoxId = Guid.NewGuid();

            bool closedByApi = false;
            bool closedByTimeout = false;

            try {
                // Set the close action so buttons can close the window
                msgBoxResult.SetCloseAction(() => service?.Close());

                // Register this message box in the global registry
                var registrationId = MessageBoxRegistry.Register(
                    Text,
                    service,
                    () => {
                        closedByApi = true;
                        service?.Close();
                    }
                );

                currentMessageBoxId = registrationId;

                Logger.Info($"TNS MessageBox displayed with ID: {registrationId}");
                Logger.Info($"TNS MessageBox - Timeout enabled: {CloseOnTimeout}, Timeout: {TimeoutSeconds}s, Continue on timeout: {ContinueOnTimeout}");

                // Start timeout task if enabled
                Task timeoutTask = null;
                if (CloseOnTimeout && TimeoutSeconds > 0) {
                    Logger.Info($"TNS MessageBox - Starting timeout task for {TimeoutSeconds} seconds");
                    timeoutTask = Task.Run(async () => {
                        try {
                            await Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), token);
                            if (!token.IsCancellationRequested) {
                                closedByTimeout = true;
                                msgBoxResult.Continue = ContinueOnTimeout;
                                Logger.Info($"TNS MessageBox - Timeout triggered after {TimeoutSeconds} seconds, Continue: {ContinueOnTimeout}");
                                service?.Close();
                            }
                        } catch (TaskCanceledException) {
                            Logger.Info($"TNS MessageBox - Timeout task cancelled");
                        }
                    });
                }

                // Register cancellation handler for user/API cancellation
                using (token.Register(() => {
                    Logger.Info($"TNS MessageBox - User/API cancellation triggered");
                    service?.Close();
                })) {
                    Logger.Info($"TNS MessageBox - Showing dialog");
                    await service.ShowDialog(msgBoxResult, "Touch 'N' Stars Message");
                    Logger.Info($"TNS MessageBox - Dialog closed");
                }

                // Wait for timeout task to complete if it's running
                if (timeoutTask != null && !timeoutTask.IsCompleted) {
                    try {
                        await timeoutTask;
                    } catch (Exception ex) {
                        Logger.Debug($"TNS MessageBox - Timeout task exception: {ex.Message}");
                    }
                }

                // Check if closed by API
                if (closedByApi) {
                    var registration = MessageBoxRegistry.Get(registrationId);
                    if (registration != null) {
                        msgBoxResult.Continue = registration.ContinueOnClose;
                        Logger.Info($"TNS MessageBox closed by API - Continue: {msgBoxResult.Continue}");
                    }
                }

            } finally {
                // Always unregister when done
                if (currentMessageBoxId.HasValue) {
                    MessageBoxRegistry.Unregister(currentMessageBoxId.Value);
                    currentMessageBoxId = null;
                }
            }

            token.ThrowIfCancellationRequested();

            // Handle the result
            if (!msgBoxResult.Continue && !closedByTimeout) {
                Logger.Info("TNS MessageBox: User cancelled - Stopping Sequence");
                var root = ItemUtility.GetRootContainer(this.Parent);
                root?.Interrupt();
            } else if (closedByTimeout && !ContinueOnTimeout) {
                Logger.Info("TNS MessageBox: Timeout - Stopping Sequence");
                var root = ItemUtility.GetRootContainer(this.Parent);
                root?.Interrupt();
            } else if (closedByApi) {
                var registration = MessageBoxRegistry.Get(currentMessageBoxId ?? Guid.Empty);
                if (registration != null && !registration.ContinueOnClose) {
                    Logger.Info("TNS MessageBox: API closed with stop - Stopping Sequence");
                    var root = ItemUtility.GetRootContainer(this.Parent);
                    root?.Interrupt();
                }
            }
        }

        public override string ToString() {
            return $"Category: {Category}, Item: TNS MessageBox, Text: {Text}";
        }
    }

    public class TNSMessageBoxResult : BaseINPC {
        private Action closeAction;

        public TNSMessageBoxResult(string message) {
            this.Message = message;
            Continue = true;
        }

        public void SetCloseAction(Action action) {
            closeAction = action;
            ContinueCommand = new RelayCommand((object o) => {
                Continue = true;
                closeAction?.Invoke();
            });
            CancelCommand = new RelayCommand((object o) => {
                Continue = false;
                closeAction?.Invoke();
            });
        }

        public string Message { get; }
        public bool Continue { get; set; }

        public ICommand ContinueCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
    }
}
