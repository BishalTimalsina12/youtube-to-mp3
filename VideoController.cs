using FFMpegCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using songplayer.Models;
using VideoLibrary;
using System.IO;
using System;
using System.Threading.Tasks;

public class VideoController : Controller
{
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly IHubContext<ProgressHub> _hubContext;

    public VideoController(IWebHostEnvironment hostingEnvironment, IHubContext<ProgressHub> hubContext)
    {
        _hostingEnvironment = hostingEnvironment;
        _hubContext = hubContext;
    }

    public ActionResult Index()
    {
        return View(new VideoDownloadModel());
    }

    [HttpPost]
    public async Task<IActionResult> ConvertToMp3(VideoDownloadModel model)
    {
        try
        {
            var youtubeService = YouTube.Default;
            var video = youtubeService.GetVideo(model.YouTubeUrl);

            // Notify that the process has initiated
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", "Process initiated");

            // Notify that the download is starting
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", "Converting video...");

            byte[] videoBytes = video.GetBytes();

            // Notify that the download is complete
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", "Download complete. Starting conversion...");

            string tempDirectory = Path.Combine(_hostingEnvironment.WebRootPath, "Temp");
            Directory.CreateDirectory(tempDirectory);
            string videoPath = Path.Combine(tempDirectory, video.FullName);
            await System.IO.File.WriteAllBytesAsync(videoPath, videoBytes);

            string outputPath = Path.ChangeExtension(videoPath, ".mp3");

            await FFMpegArguments
                  .FromFileInput(videoPath)
                  .OutputToFile(outputPath, true, options => options
                      .WithAudioCodec("libmp3lame"))
                  .NotifyOnProgress(async (percentage) =>
                  {
                      // Real-time progress update
                      await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"Converting... {percentage:P2} complete");
                  }, TimeSpan.FromSeconds(1))
                  .ProcessAsynchronously();

            // Notify conversion is complete
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", "Conversion complete.  Downloading music...");

            // Clean up and prepare the file for download
            var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(outputPath, FileMode.Open))
            {
                await fileStream.CopyToAsync(memoryStream);
            }

            System.IO.File.Delete(videoPath);
            System.IO.File.Delete(outputPath);

            memoryStream.Position = 0;
            return new FileStreamResult(memoryStream, "audio/mpeg")
            {
                FileDownloadName = $"{Path.GetFileNameWithoutExtension(video.FullName)}.mp3"
            };
        }
        catch (Exception ex)
        {
            // Notify in case of error
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"Error: {ex.Message}");
            ModelState.AddModelError("", "Failed to convert video. " + ex.Message);
            return View("Index", model);
        }
    }
}