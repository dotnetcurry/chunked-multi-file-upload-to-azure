using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AzureBlobChunkedFileUpload.Models
{
    public class CloudFilesModel
    {
        public CloudFilesModel()
            : this(null)
        {
            Files = new List<CloudFile>();
        }

        public CloudFilesModel(IEnumerable<IListBlobItem> list)
        {
            Files = new List<CloudFile>();
            try
            {
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        CloudFile info = CloudFile.CreateFromIListBlobItem(item);
                        if (info != null)
                        {
                            Files.Add(info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore Errors when empty
            }
        }
        public List<CloudFile> Files { get; set; }
    }
}