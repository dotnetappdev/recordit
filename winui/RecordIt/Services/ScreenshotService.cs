using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Microsoft.Graphics.Canvas;
using WinRT.Interop;

namespace RecordIt.Services;

public class ScreenshotService
{
    /// <summary>
    /// Takes a screenshot of the specified capture item and saves it to the Pictures library.
    /// </summary>
    public static async Task<string?> TakeScreenshotAsync(GraphicsCaptureItem captureItem, IntPtr windowHandle)
    {
        try
        {
            // Create a frame pool for the capture
            var device = CanvasDevice.GetSharedDevice();
            using var framePool = Direct3D11CaptureFramePool.Create(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, captureItem.Size);

            using var session = framePool.CreateCaptureSession(captureItem);
            
            var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
            
            framePool.FrameArrived += (s, a) =>
            {
                try
                {
                    var frame = framePool.TryGetNextFrame();
                    if (frame != null && !tcs.Task.IsCompleted)
                    {
                        tcs.TrySetResult(frame);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            session.StartCapture();
            
            // Wait for first frame (with timeout)
            var delayTask = Task.Delay(TimeSpan.FromSeconds(5));
            var completedTask = await Task.WhenAny(tcs.Task, delayTask);
            
            if (completedTask == delayTask)
            {
                return null; // Timeout
            }

            using var frame = await tcs.Task;
            
            // Convert to SoftwareBitmap
            var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            // Generate filename
            var filename = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var folder = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);
            var file = await folder.SaveFolder.CreateFileAsync(filename, CreationCollisionOption.GenerateUniqueName);

            // Encode and save
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            
            encoder.SetSoftwareBitmap(softwareBitmap);
            encoder.IsThumbnailGenerated = true;
            
            await encoder.FlushAsync();
            
            return file.Path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Shows a file save picker for custom screenshot location
    /// </summary>
    public static async Task<string?> TakeScreenshotWithPickerAsync(GraphicsCaptureItem captureItem, IntPtr windowHandle)
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, windowHandle);
            
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.SuggestedFileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}";
            picker.FileTypeChoices.Add("PNG Image", new[] { ".png" });
            picker.FileTypeChoices.Add("JPEG Image", new[] { ".jpg", ".jpeg" });
            picker.FileTypeChoices.Add("BMP Image", new[] { ".bmp" });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return null;

            // Capture frame
            var device = CanvasDevice.GetSharedDevice();
            using var framePool = Direct3D11CaptureFramePool.Create(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, captureItem.Size);

            using var session = framePool.CreateCaptureSession(captureItem);
            
            var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
            
            framePool.FrameArrived += (s, a) =>
            {
                try
                {
                    var frame = framePool.TryGetNextFrame();
                    if (frame != null && !tcs.Task.IsCompleted)
                    {
                        tcs.TrySetResult(frame);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            session.StartCapture();
            
            var delayTask = Task.Delay(TimeSpan.FromSeconds(5));
            var completedTask = await Task.WhenAny(tcs.Task, delayTask);
            
            if (completedTask == delayTask)
                return null;

            using var frame = await tcs.Task;
            var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            // Determine encoder based on extension
            Guid encoderId = file.FileType.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => BitmapEncoder.JpegEncoderId,
                ".bmp" => BitmapEncoder.BmpEncoderId,
                _ => BitmapEncoder.PngEncoderId
            };

            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
            
            encoder.SetSoftwareBitmap(softwareBitmap);
            encoder.IsThumbnailGenerated = true;
            
            // JPEG quality
            if (encoderId == BitmapEncoder.JpegEncoderId)
            {
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                var propertySet = new Windows.Graphics.Imaging.BitmapPropertySet();
                var qualityValue = new Windows.Graphics.Imaging.BitmapTypedValue(0.95, Windows.Foundation.PropertyType.Single);
                propertySet.Add("ImageQuality", qualityValue);
                await encoder.BitmapProperties.SetPropertiesAsync(propertySet);
            }
            
            await encoder.FlushAsync();
            
            return file.Path;
        }
        catch
        {
            return null;
        }
    }
}
