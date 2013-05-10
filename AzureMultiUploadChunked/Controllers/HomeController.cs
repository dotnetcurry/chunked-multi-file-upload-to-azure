using AzureBlobChunkedFileUpload.Models;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace AzureBlobChunkedFileUpload.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));
            CloudBlobClient storageClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer storageContainer = storageClient.GetContainerReference(
                ConfigurationManager.AppSettings.Get("CloudStorageContainerReference"));
            CloudFilesModel blobsList = new
                CloudFilesModel(storageContainer.ListBlobs(useFlatBlobListing: true));
            return View(blobsList);
        }

        public ActionResult UploadFile()
        {
            return View();
        }

        [HttpPost]
        public ActionResult SetMetadata(int blocksCount, string fileName, long fileSize, int fileIndex)
        {
            var container = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings["StorageConnectionString"]).CreateCloudBlobClient()
                .GetContainerReference(ConfigurationManager.AppSettings["CloudStorageContainerReference"]);
            container.CreateIfNotExists();
            var fileToUpload = new CloudFile()
            {
                BlockCount = blocksCount,
                FileName = fileName,
                Size = fileSize,
                BlockBlob = container.GetBlockBlobReference(fileName),
                StartTime = DateTime.Now,
                IsUploadCompleted = false,
                UploadStatusMessage = string.Empty,
                FileKey = "CurrentFile" + fileIndex.ToString(),
                FileIndex = fileIndex
            };
            Session.Add(fileToUpload.FileKey, fileToUpload);
            return Json(new { success = true, index = fileIndex });
        }

        [HttpPost]
        [ValidateInput(false)]
        public ActionResult UploadChunk(int id, int fileIndex)
        {
            HttpPostedFileBase request = Request.Files["Slice"];
            byte[] chunk = new byte[request.ContentLength];
            request.InputStream.Read(chunk, 0, Convert.ToInt32(request.ContentLength));
            JsonResult returnData = null;
            string fileSession = "CurrentFile" + fileIndex.ToString();
            if (Session[fileSession] != null)
            {
                CloudFile model = (CloudFile)Session[fileSession];
                returnData = UploadCurrentChunk(model, chunk, id);
                if (returnData != null)
                {
                    return returnData;
                }
                if (id == model.BlockCount)
                {
                    return CommitAllChunks(model);
                }
            }
            else
            {
                returnData = Json(new
                {
                    error = true,
                    isLastBlock = false,
                    message = string.Format(CultureInfo.CurrentCulture,
                        "Failed to Upload file.", "Session Timed out")
                });
                return returnData;
            }

            return Json(new { error = false, isLastBlock = false, message = string.Empty, index = fileIndex });
        }

        private ActionResult CommitAllChunks(CloudFile model)
        {
            model.IsUploadCompleted = true;
            bool errorInOperation = false;
            try
            {
                var blockList = Enumerable.Range(1, (int)model.BlockCount).ToList<int>().ConvertAll(rangeElement =>
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                                string.Format(CultureInfo.InvariantCulture, "{0:D4}", rangeElement))));
                model.BlockBlob.PutBlockList(blockList);
                var duration = DateTime.Now - model.StartTime;
                float fileSizeInKb = model.Size / 1024;
                string fileSizeMessage = fileSizeInKb > 1024 ?
                    string.Concat((fileSizeInKb / 1024).ToString(CultureInfo.CurrentCulture), " MB") :
                    string.Concat(fileSizeInKb.ToString(CultureInfo.CurrentCulture), " KB");
                model.UploadStatusMessage = string.Format(CultureInfo.CurrentCulture,
                    "File uploaded successfully. {0} took {1} seconds to upload",
                    fileSizeMessage, duration.TotalSeconds);
            }
            catch (StorageException e)
            {
                model.UploadStatusMessage = "Failed to Upload file. Exception - " + e.Message;
                errorInOperation = true;
            }
            finally
            {
                Session.Remove(model.FileKey);
            }
            return Json(new
            {
                error = errorInOperation,
                isLastBlock = model.IsUploadCompleted,
                message = model.UploadStatusMessage,
                index = model.FileIndex
            });
        }

        private JsonResult UploadCurrentChunk(CloudFile model, byte[] chunk, int id)
        {
            using (var chunkStream = new MemoryStream(chunk))
            {
                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        string.Format(CultureInfo.InvariantCulture, "{0:D4}", id)));
                try
                {
                    model.BlockBlob.PutBlock(
                        blockId,
                        chunkStream, null, null,
                        new BlobRequestOptions()
                        {
                            RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(10), 3)
                        },
                        null);
                    return null;
                }
                catch (StorageException e)
                {
                    Session.Remove(model.FileKey);
                    model.IsUploadCompleted = true;
                    model.UploadStatusMessage = "Failed to Upload file. Exception - " + e.Message;
                    return Json(new { error = true, isLastBlock = false, message = model.UploadStatusMessage, index = model.FileIndex });
                }
            }
        }
    }
}
