/// <reference path="jquery-1.8.2.js" />
/// <reference path="_references.js" />
$(document).ready(function ()
{
    $(document).on("click", "#fileUpload", beginUpload);
    ko.applyBindings(uploaders);
});

var beginUpload = function ()
{
    var fileControl = document.getElementById("selectFile");
    if (fileControl.files.length > 0)
    {
        uploaders.uploaderCollection.removeAll();
        for (var i = 0; i < fileControl.files.length; i++)
        {
            cful = Object.create(chunkedFileUploader);
            cful.init(fileControl.files[i], i);
            uploaders.uploaderCollection.push(cful);
        }
        $(".progressBar").progressbar(0);
        uploaders.uploadAll();
    }
}

var uploaders = {
    uploaderCollection: ko.observableArray([]),
    uploadAll: function ()
    {
        for (var i = 0; i < this.uploaderCollection().length; i++)
        {
            var cful = this.uploaderCollection()[i];
            cful.uploadMetaData();
        }
    }
}

var chunkedFileUploader =
{
    maxRetries: 3,
    blockLength: 1048576,
    numberOfBlocks: 1,
    currentChunk: 1,
    retryAfterSeconds: 3,
    fileToBeUploaded: null,
    size: 0,
    fileIndex: 0,
    name: "",

    init: function (file, index)
    {
        this.fileToBeUploaded = file;
        this.size = file.size;
        this.name = file.name;
        this.fileIndex = index;
    },

    uploadMetaData: function ()
    {
        this.numberOfBlocks = Math.ceil(this.size / this.blockLength);
        this.currentChunk = 1;

        $.ajax({
            type: "POST",
            async: false,
            url: "/Home/SetMetadata?blocksCount=" + this.numberOfBlocks
                + "&fileName=" + this.name
                + "&fileSize=" + this.size
                + "&fileIndex=" + this.fileIndex,
        }).done(function (state)
        {
            if (state.success == true)
            {
                var cufl = uploaders.uploaderCollection()[state.index]
                cufl.displayStatusMessage(cufl, "Starting Upload");
                cufl.sendFile(cufl);
            }
        }).fail(function ()
        {
            this.displayStatusMessage("Failed to send MetaData");
        });

    },

    sendFile: function (uploader)
    {
        var start = 0,
            end = Math.min(uploader.blockLength, uploader.fileToBeUploaded.size),
            retryCount = 0,
            sendNextChunk, fileChunk;
        this.displayStatusMessage(uploader,"");

        var cful = uploader;

        sendNextChunk = function ()
        {
            fileChunk = new FormData();

            if (uploader.fileToBeUploaded.slice)
            {
                fileChunk.append('Slice', uploader.fileToBeUploaded.slice(start, end));
            }
            else if (uploader.fileToBeUploaded.webkitSlice)
            {
                fileChunk.append('Slice', uploader.fileToBeUploaded.webkitSlice(start, end));
            }
            else if (uploader.fileToBeUploaded.mozSlice)
            {
                fileChunk.append('Slice', uploader.fileToBeUploaded.mozSlice(start, end));
            }
            else
            {
                displayStatusMessage(cful, operationType.UNSUPPORTED_BROWSER);
                return;
            }
            jqxhr = $.ajax({
                async: true,
                url: ('/Home/UploadChunk?id=' + uploader.currentChunk + "&fileIndex=" + uploader.fileIndex),
                data: fileChunk,
                cache: false,
                contentType: false,
                processData: false,
                type: 'POST'
            }).fail(function (request, error)
            {
                if (error !== 'abort' && retryCount < maxRetries)
                {
                    ++retryCount;
                    setTimeout(sendNextChunk, retryAfterSeconds * 1000);
                }
                if (error === 'abort')
                {
                    displayStatusMessage(cful, "Aborted");
                }
                else
                {
                    if (retryCount === maxRetries)
                    {
                        displayStatusMessage(cful, "Upload timed out.");
                        resetControls();
                        uploader = null;
                    }
                    else
                    {
                        displayStatusMessage(cful, "Resuming Upload");
                    }
                }
                return;
            }).done(function (state)
            {

                if (state.error || state.isLastBlock)
                {
                    cful.displayStatusMessage(cful, state.message);
                    return;
                }
                ++cful.currentChunk;
                start = (cful.currentChunk - 1) * cful.blockLength;
                end = Math.min(cful.currentChunk * cful.blockLength, cful.fileToBeUploaded.size);
                retryCount = 0;
                cful.updateProgress(cful);
                if (cful.currentChunk <= cful.numberOfBlocks)
                {
                    sendNextChunk();
                }
            });
        }
        sendNextChunk();
    },

    displayStatusMessage: function (uploader, message)
    {
        $("#statusMessage" + uploader.fileIndex).text(message);
    },

    updateProgress: function (uploader)
    {
        var progress = uploader.currentChunk / uploader.numberOfBlocks * 100;
        if (progress <= 100)
        {
            $("#progressBar" + uploader.fileIndex).progressbar("option", "value", parseInt(progress));
            uploader.displayStatusMessage(uploader, "Uploaded " + progress + "%");
        }
    }
}