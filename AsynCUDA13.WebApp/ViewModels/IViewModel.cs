using AsynCUDA13.Client;
using AsynCUDA13.Shared.CudaDtos;
using AsynCUDA13.WebApp.Components.Dialogs;
using Microsoft.JSInterop;
using Radzen;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AsynCUDA13.WebApp.ViewModels
{
    public interface IViewModel
    {
        event Action? StateChanged;
    }

    public abstract class ViewModelBase<TRequest, TResponse> : IViewModel where TRequest : class where TResponse : class
    {
        /// <summary>
        /// The API client used for making requests to the backend.
        /// </summary>
        protected readonly ApiClient Api;

        /// <summary>
        /// The JavaScript runtime used for invoking JavaScript functions from .NET code. DI injected in the constructor.
        /// </summary>
        protected readonly IJSRuntime Js;

        /// <summary>
        /// The maximum upload size in kilobytes. This value is used to limit the size of files that can be uploaded through the view model. The default value is set to 16 MB (16 * 1024 KB).
        /// </summary>
        public readonly int MaxUploadKb = 65536;

        /// <summary>
        /// Gets the current CUDA context information. This property is updated when the state of the view model changes with optional context refresh. It may be null if the context information has not been retrieved yet or if CUDA is not available.
        /// </summary>
        protected CudaContextInfo? _contextInfo = null;

        /// <summary>
        /// Gets a value indicating whether CUDA is initialized and available for use. This property checks the Online status of the current CUDA context information. It returns true if CUDA is initialized and online, and false otherwise.
        /// </summary>
        public bool IsCudaInitialized => this._contextInfo?.Online == true;

        /// <summary>
        /// Event that is triggered when the state of the view model changes. Subscribers can use this event to update their UI or perform other actions in response to state changes.
        /// </summary>
        public event Action? StateChanged;

        /// <summary>
        /// Gets or sets whether the RadzenDialog shall be shown. Returns false if no dialog is attached.
        /// </summary>
        public bool ShowDialog
        {
            get => this.Dialog?.Visible ?? false;
            set
            {
                if (this.Dialog is not null)
                {
                    this.Dialog.Visible = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the RadzenDialog component associated with this view model.
        /// Set via @ref from the hosting .razor page after render.
        /// Null if no dialog is associated with this view model.
        /// </summary>
        public IRadzenDialog<TRequest, TResponse>? Dialog { get; set; }

        /// <summary>
        /// Gets the current information message to be displayed in the view model. This message can be used to provide feedback to the user, such as success, warning, or error messages. It may be null if there is no message to display.
        /// </summary>
        public string? InfoMessage { get; private set; } = null;

        /// <summary>
        /// Gets the severity level of the current information message. This property can be used to determine the appropriate styling or icon to display for the message.
        /// </summary>
        public AlertStyle InfoSeverity { get; private set; } = AlertStyle.Info;

        /// <summary>
        /// Gets a value indicating whether there is an information message to display. This property returns true if the InfoMessage property is not null or empty, and false otherwise.
        /// </summary>
        public bool ShowInfoMessage => !string.IsNullOrEmpty(this.InfoMessage);
        private bool _showInfoCloseButton = true;

        /// <summary>
        /// Gets a string representation of whether the close button for the information message should be shown. This property returns "true" if the close button should be displayed, and "false" otherwise.
        /// </summary>
        public string ShowInfoCloseButton => this._showInfoCloseButton ? "true" : "false";
        private DateTime? _hideInfoMessageAt = null;
        private readonly TimerCallback TimerEvent;

        /// <summary>
        /// Gets the timer used to automatically hide the information message after a specified duration. This timer is started when the HideInfoMessageAt property is set to a future time, and it stops when the message is hidden or cleared.
        /// </summary>
        public Timer? InfoMessageTimer { get; private set; } = null;

        /// <summary>
        /// Gets the time at which the information message should be automatically hidden. If this property is set to a future time, the InfoMessageTimer will be started to hide the message when the time is reached. If it is set to null, the timer will be stopped and the message will remain visible until manually closed or a new message is set.
        /// </summary>
        public DateTime? HideInfoMessageAt
        {
            get
            {
                return this._hideInfoMessageAt;
            }
            private set
            {
                this._hideInfoMessageAt = value;
                if (value.HasValue)
                {
                    if (this.InfoMessageTimer == null)
                    {
                        this.InfoMessageTimer = new Timer(this.TimerEvent, null, Timeout.Infinite, Timeout.Infinite);
                    }
                    this.InfoMessageTimer.Change(0, 1000);
                }
                else
                {
                    this.InfoMessageTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Gets the countdown in seconds until the information message is automatically hidden. If the HideInfoMessageAt property is set to a future time, this property returns the number of seconds remaining until that time. If HideInfoMessageAt is null or in the past, this property returns null.
        /// </summary>
        public Double? InfoMessageTimerCountdown => this.HideInfoMessageAt.HasValue ? (this.HideInfoMessageAt.Value - DateTime.Now).TotalSeconds : null;

        protected ViewModelBase(ApiClient api, IJSRuntime js, int maxUploadKb = 65536)
        {
            this.Api = api;
            this.Js = js;
            this.MaxUploadKb = maxUploadKb;

            this.TimerEvent = new TimerCallback(async (state) =>
            {
                if (this.HideInfoMessageAt.HasValue && DateTime.Now >= this.HideInfoMessageAt.Value)
                {
                    this.InfoMessage = null;
                    this.HideInfoMessageAt = null;
                    this.InfoMessageTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    await this.NotifyStateChangedAsync(false);
                }
            });
        }

        /// <summary>
        /// Notifies subscribers that the state of the view model has changed. Optionally refreshes the context information before notifying.
        /// </summary>
        /// <param name="refreshContextInfo">If true, refreshes the context information before notifying subscribers.</param>
        protected void NotifyStateChanged(bool refreshContextInfo = true)
        {
            if (refreshContextInfo)
            {
                this._contextInfo = this.Api.GetCudaContextInfoAsync().ConfigureAwait(true).GetAwaiter().GetResult();
            }
            this.StateChanged?.Invoke();
        }

        /// <summary>
        /// Notifies subscribers that the state of the view model has changed. Optionally refreshes the context information before notifying.
        /// </summary>
        /// <param name="refreshContextInfo">If true, refreshes the context information before notifying subscribers.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected async Task NotifyStateChangedAsync(bool refreshContextInfo = true)
        {
            if (refreshContextInfo)
            {
                this._contextInfo = await this.Api.GetCudaContextInfoAsync();
            }
            this.StateChanged?.Invoke();
        }

        /// <summary>
        /// Updates the information message displayed in the view model with the specified parameters.
        /// </summary>
        /// <param name="message">The message to display. If null, the message will be cleared.</param>
        /// <param name="severity">The severity level of the message (e.g., "info", "success", "warning", "error").</param>
        /// <param name="showCloseButton">Whether to show a close button for the message.</param>
        /// <param name="secondsVisible">The number of seconds the message should be visible. Null means the message will remain visible until manually closed or new message is set.</param>
        /// <param name="notifyStateChanged">Whether to notify subscribers that the state has changed.</param>
        protected void UpdateInfoMessage(string? message, string severity = "info", bool showCloseButton = true, Double? secondsVisible = null, bool notifyStateChanged = false)
        {
            this.InfoSeverity = severity switch
            {
                "success" => AlertStyle.Success,
                "warning" => AlertStyle.Warning,
                "error" => AlertStyle.Danger,
                _ => AlertStyle.Info
            };
            this.InfoMessage = message;
            this._showInfoCloseButton = showCloseButton;
            if (secondsVisible.HasValue)
            {
                this.HideInfoMessageAt = DateTime.Now.AddSeconds(secondsVisible.Value);
            }
            else
            {
                this.HideInfoMessageAt = null;
            }

            if (notifyStateChanged)
            {
                this.NotifyStateChanged(false);
            }
        }

        /// <summary>
        /// Asynchronously updates the information message displayed in the view model with the specified parameters.
        /// </summary>
        /// <param name="message">The message to display. If null, the message will be cleared.</param>
        /// <param name="severity">The severity level of the message (e.g., "info", "success", "warning", "error").</param>
        /// <param name="showCloseButton">Whether to show a close button for the message.</param>
        /// <param name="secondsVisible">The number of seconds the message should be visible. Null means the message will remain visible until manually closed or new message is set.</param>
        /// <param name="notifyStateChanged">Whether to notify subscribers that the state has changed.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected async Task UpdateInfoMessageAsync(string? message, string severity = "info", bool showCloseButton = true, Double? secondsVisible = null, bool notifyStateChanged = false)
        {
            this.InfoSeverity = severity switch
            {
                "success" => AlertStyle.Success,
                "warning" => AlertStyle.Warning,
                "error" => AlertStyle.Danger,
                _ => AlertStyle.Info
            };
            this.InfoMessage = message;
            this._showInfoCloseButton = showCloseButton;
            if (secondsVisible.HasValue)
            {
                this.HideInfoMessageAt = DateTime.Now.AddSeconds(secondsVisible.Value);
            }
            else
            {
                this.HideInfoMessageAt = null;
            }

            if (notifyStateChanged)
            {
                await this.NotifyStateChangedAsync(false);
            }
        }

        /// <summary>
        /// Asynchronously sets an information message to be displayed in the view model with the specified parameters. This method is a convenience wrapper around UpdateInfoMessageAsync that automatically notifies subscribers of the state change.
        /// </summary>
        /// <param name="message">The message to display. If null, the message will be cleared.</param>
        /// <param name="severity">The severity level of the message (e.g., "info", "success", "warning", "error").</param>
        /// <param name="showCloseButton">Whether to show a close button for the message.</param>
        /// <param name="secondsVisible">The number of seconds the message should be visible. Null means the message will remain visible until manually closed or new message is set.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task PutInfoMessageAsync(string message, string severity = "info", bool showCloseButton = true, Double? secondsVisible = null)
        {
            await this.UpdateInfoMessageAsync(message, severity, showCloseButton, secondsVisible, true);
        }

        /// <summary>
        /// Asynchronously opens the dialog associated with this view model, passing in an optional request object. If no dialog is attached, an error message is displayed instead.
        /// </summary>
        /// <param name="request">The request object to pass to the dialog. If null, a new request may be created depending on the createRequestIfNull parameter.</param>
        /// <param name="createRequestIfNull">Whether to create a new request object if the request parameter is null.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected async Task OpenDialogAsync(TRequest? request = null, bool createRequestIfNull = true)
        {
            if (this.Dialog is not null)
            {
                await this.Dialog.OpenDialogAsync(request, createRequestIfNull);
            }
            else
            {
                await this.UpdateInfoMessageAsync("No dialog is attached to this view model.", "error", true, 5, true);
            }
        }

        /// <summary>
        /// Asynchronously closes the dialog associated with this view model. If no dialog is attached, an error message is displayed instead.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected async Task CloseDialogAsync()
        {
            if (this.Dialog is not null)
            {
                await this.Dialog.CloseDialogAsync();
            }
            else
            {
                await this.UpdateInfoMessageAsync("No dialog is attached to this view model.", "error", true, 5, true);
            }
        }
    }
}
