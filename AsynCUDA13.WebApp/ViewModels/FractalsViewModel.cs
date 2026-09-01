using AsynCUDA13.Client;
using AsynCUDA13.Shared.Api.Requests;
using AsynCUDA13.Shared.Api.Responses;
using AsynCUDA13.Shared.MediaDtos;
using AsynCUDA13.Shared.RuntimeDtos;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using AsynCUDA13.WebApp.Services;
using Microsoft.AspNetCore.Components;
using AsynCUDA13.Shared.Serialization;

namespace AsynCUDA13.WebApp.ViewModels
{
    public class FractalsViewModel : ViewModelBase<RuntimeExecuteRequest, RuntimeExecuteResponse>
    {


        public FractalsViewModel(ApiClient apiClient, IJSRuntime js)
            : base(apiClient, js)
        {
            this.InitializePanelEvents();
        }


        public RuntimeKernelInfo[] GenerativeKernels { get; set; } = [];
        public bool CanExecute => this.GenerativeKernels?.FirstOrDefault(k => k.FunctionName.Equals(this.SelectedKernelName, StringComparison.OrdinalIgnoreCase))?.ArgumentsCount >= 0;
        public RuntimeExecuteRequest ExecuteRequest { get; set; } = new();
        public RuntimeExecuteResponse? ExecuteResponse { get; set; } = null;

        public string? CurrentImageBase64Data { get; set; }
        public RadzenImagePanelOptions PanelOptions { get; set; } = new();
        public int ImageWidth { get; set; } = 1024;
        public int ImageHeight { get; set; } = 1024;

        public string? SelectedKernelName
        {
            get;
            set
            {
                this.ExecuteRequest.KernelInfo = this.GenerativeKernels?.FirstOrDefault(k => k.FunctionName.Equals(value, StringComparison.OrdinalIgnoreCase));
                field = value;
            }
        }

        public string ImageFormat { get; set; } = "png";

        private void InitializePanelEvents()
        {
            var factory = new EventCallbackFactory();
            this.PanelOptions.OnWheel = factory.Create<WheelEventArgs>(this, this.HandlePanelWheel);
            this.PanelOptions.OnMouseMove = factory.Create<MouseEventArgs>(this, this.HandlePanelMouseMove);
            this.PanelOptions.OnMouseDown = factory.Create<MouseEventArgs>(this, this.HandlePanelMouseDown);
        }

        private async Task HandlePanelWheel(WheelEventArgs e)
        {
            if (e.CtrlKey)
            {
                // Beispiel: Ctrl + Wheel ändert maxIter (Argument index 2 angenommen)
                this.UpdateKernelArgument(2, e.DeltaY > 0 ? -1 : 1);
            }
            else
            {
                // Standard Zoom
                this.UpdateKernelArgument(1, e.DeltaY > 0 ? -0.1 : 0.1);
            }
            await this.ExecuteAndRefreshAsync();
        }

        private async Task HandlePanelMouseDown(MouseEventArgs e)
        {
            // Logik für Drag-Start
        }

        private async Task HandlePanelMouseMove(MouseEventArgs e)
        {
            if (e.Buttons == 1) // Linksklick gehalten -> Pan
            {
                this.UpdateKernelArgument(0, e.OffsetX * 0.01); // X-Offset
                this.UpdateKernelArgument(3, e.OffsetY * 0.01); // Y-Offset
                await this.ExecuteAndRefreshAsync();
            }
        }

        private void UpdateKernelArgument(int index, double delta)
        {
            if (this.ExecuteRequest.KernelInfo == null || index < 0 || index >= this.ExecuteRequest.KernelInfo.ArgumentsCount)
            {
                return;
            }

            var typedValues = DataParser.ParseArgumentValues(this.ExecuteRequest.ArgumentValues, this.ExecuteRequest.KernelInfo);

            if (this.ExecuteRequest[index] == null)
            {

            }

            if (typedValues[index] is double currentVal)
            {
                this.ExecuteRequest.ArgumentValues[index] = (currentVal + delta).ToString();
            }
            else if (typedValues[index] is float fVal)
            {
                this.ExecuteRequest.ArgumentValues[index] = (fVal + (float) delta).ToString();
            }
            else if (typedValues[index] is long lVal)
            {
                this.ExecuteRequest.ArgumentValues[index] = (lVal + (long) Math.Round(delta)).ToString();
            }
            else if (typedValues[index] is int iVal)
            {
                this.ExecuteRequest.ArgumentValues[index] = (iVal + (int) Math.Round(delta)).ToString();
            }
            else
            {
                this.ExecuteRequest.ArgumentValues[index] = delta.ToString();
            }
        }

        private async Task ExecuteAndRefreshAsync()
        {
            try
            {
                var response = await this.ExecuteAsync();
                if (response?.ResultPointers?.Length > 0)
                {
                    var mem = await this.Api.GetMemoryInfoAsync(response.ResultPointers.First());
                    if (mem == null)
                    {
                        await this.PutInfoMessageAsync("Failed to retrieve memory info for the generated image.", "error", true);
                    }
                    else
                    {
                        var pullResult = await this.Api.PullAsync(mem.IndexPointer);
                        if (pullResult == null || !pullResult.Success)
                        {
                            await this.PutInfoMessageAsync("Failed to pull the generated image data.", "error", true);

                        }
                        else
                        {
                            var imageData = await this.Api.GetImageDataAsync((await this.Api.GetAssetIdForIndexPointerAsync(mem.IndexPointer)).ToString() ?? string.Empty, this.ImageFormat, false);
                            if (imageData == null)
                            {
                                await this.PutInfoMessageAsync("No image data received from the kernel execution.", "error", true);
                            }
                            else
                            {
                                this.CurrentImageBase64Data = imageData.Base64Image;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await this.PutInfoMessageAsync($"Execution failed: {ex.Message}", "error", true);
            }

            await this.NotifyStateChangedAsync(true);
        }



        public async Task LoadGenerativeImageKernelsAsync()
        {
            this.GenerativeKernels = (await this.Api.GetKernelsAsync()).Where(k => k.PointerArgumentsCount == 1 && k.PointerArgumentTypes.Values.FirstOrDefault()?.Contains("byte", StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (this.GenerativeKernels.Length <= 0)
            {
                await this.PutInfoMessageAsync("No generative image kernels found. Please upload a kernel first.", "info", false);
            }

            await this.NotifyStateChangedAsync();
        }

        public async Task<RuntimeExecuteResponse?> ExecuteAsync()
        {
            return await this.Api.ExecuteGenericKernelAsync(this.SelectedKernelName, this.ExecuteRequest.ArgumentValues, false, true);
        }
    }
}