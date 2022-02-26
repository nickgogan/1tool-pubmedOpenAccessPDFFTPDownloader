using FluentFTP;
using Humanizer;
using System.Diagnostics;

namespace FTPDownloader
{
    internal class App
    {
        private const long ONE_HUNDRED_KILOBYTES_IN_BYTES = 100;
        private const long TEN_MEGABYTES_IN_BYTES = 10_000_000;
        private const long FIFTEEN_MEGABYTES_IN_BYTES = 5_000_000; //Max MDB doc size is 5MB. Leave 1MB for fields other than fulltext.
        private const long MAX_ALLOWED_SPACE_IN_BYTES = 100_000_000_000;
        private const long MAX_NUMBER_OF_FILES = 1000;//1_000;
        private const string PDF_DOWNLOAD_DIRECTORY = @"C:\0.Personal\Tech\Projects\CSharp\tool-pubmedOpenAccessPDFFTPDownloader\PDFs";
        private const string FTP_CONTENT_PATHS_FILE = @"C:\0.Personal\Tech\Projects\CSharp\tool-pubmedOpenAccessPDFFTPDownloader";

        private static void Main(string[] args)
        {
            FtpClient client = new("ftp.ncbi.nlm.nih.gov");
            try
            {
                client.AutoConnect();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error connecting to the FTP client.");
                Console.Write(ex.Message);
            }

            //Don't invoke if the file exists and matches the current FTP directory structure.
            //ContentDirectoryGenerator generator = new();
            //generator.GetContentDirectories(client, "/pub/pmc/oa_pdf/"); //Generates ftpContentPaths.txt file in /bin/Debug/net6.0/.

            List<string> contentDirectories = File.ReadLines(Path.Combine(FTP_CONTENT_PATHS_FILE, "ftpContentPaths.txt")).ToList();
            Dictionary<string, long> filesToDownload = new();
            long availableDiskSpace = MAX_ALLOWED_SPACE_IN_BYTES;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            for(int i = 0; i < contentDirectories.Count; i++)
            {
                var files = GetFtpItems(client, contentDirectories[i]);
                if (files == null) { continue; }

                foreach (var file in files)
                {
                    if (FileEnumerationDone(file, availableDiskSpace, filesToDownload.Count))
                    {
                        i = contentDirectories.Count; //Break out of outer loop.
                        break;
                    }

                    if (FileIsNotDownloadable(file, ONE_HUNDRED_KILOBYTES_IN_BYTES, TEN_MEGABYTES_IN_BYTES)) //Only download files between 10MB and 15MB
                    {
                        //Console.WriteLine("File [" + file.FullName + "] does not meet requirements for download."); //Test
                        continue;
                    }
                    else
                    {
                        Console.WriteLine("Queued file [" + file.FullName + "] of size [" + file.Size.Megabytes() + " MB] for download.");
                        filesToDownload.Add(file.FullName, file.Size);
                        availableDiskSpace -= file.Size;
                    }
                }
            }
            stopwatch.Stop();
            Console.WriteLine("Enumarting downloadable files took [" + stopwatch.ElapsedMilliseconds + " milliseconds].");
            Console.WriteLine("Number of files to download [" + filesToDownload.Count + "].");

            Console.WriteLine("Starting to download files...");

            long consumedDiskSpace = 0;
            stopwatch.Reset();
            stopwatch.Start();
            foreach (var file in filesToDownload)
            {
                if (DownloadFile(client, file.Key))
                {
                    consumedDiskSpace += file.Value;
                    Console.WriteLine("Downloaded file [" + file.Key + "].");
                }
            }
            stopwatch.Stop();
            Console.WriteLine("Downloading the files took [" + stopwatch.ElapsedMilliseconds + " milliseconds].");
            Console.WriteLine("Downloaded [" + filesToDownload.Count + "] files, consuming [" + consumedDiskSpace.Megabytes() + " MB] of space at [" + PDF_DOWNLOAD_DIRECTORY + "].");

            Console.WriteLine("Archiving and compressing PDF download directory...");

        }

        private static bool FileIsNotDownloadable(FtpListItem file, long minimumFileSize, long maximumFileSize)
        {
            if (FileDoesNotExceedMinimumSize(file.Size, minimumFileSize)) { return true; }
            if (FileExceedsMaximumSize(file.Size, maximumFileSize)) { return true; }
            return false;
        }

        private static bool FileIsScannedPdf(FtpListItem file)
        {
            bool result = false;

            

            Console.WriteLine("[FileIsScannedPdf] File [" + file.FullName + "] contains majority text. Skipping..");

            return result;
        }
        private static bool FileEnumerationDone(FtpListItem file, long availableDiskSpace, int numberOfFilesToDownload) => FileExceedsDiskSpaceOrMaxNumberOfFilesToDownload(file.FullName, file.Size, availableDiskSpace, numberOfFilesToDownload);

        private static bool FileDoesNotExceedMinimumSize(long fileSize, long minimumFileSize) => fileSize < minimumFileSize;

        private static bool FileExceedsMaximumSize(long fileSize, long maximumFileSize) => fileSize > maximumFileSize;

        private static bool FileExceedsDiskSpaceOrMaxNumberOfFilesToDownload(string fileName, long fileSize, long availableDiskSpace, long numberOfFilesToDownload)
        {
            if (availableDiskSpace - fileSize < 0)
            {
                Console.WriteLine("[FileExceedsDiskSpaceOrMaxNumberOfFilesToDownload] Not enough disk space for file [" + fileName + "].");
                return true;
            }
            if (numberOfFilesToDownload > MAX_NUMBER_OF_FILES - 1)
            {
                Console.WriteLine("[FileExceedsDiskSpaceOrMaxNumberOfFilesToDownload] Reached maximum number of files to download [" + MAX_NUMBER_OF_FILES + "].");
                return true;
            }

            return false;
        }

        private static bool DownloadFile(FtpClient client, string pathtoFile)
        {
            try
            {
                client.DownloadFile(Path.Combine(PDF_DOWNLOAD_DIRECTORY, Path.GetFileName(pathtoFile)), pathtoFile, FtpLocalExists.Overwrite, FtpVerify.Retry);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DownloadFile] Error downloading file [" + pathtoFile + "].");
                Console.Write(ex.StackTrace);
                return false;
            }
        }

        private static FtpListItem[]? GetFtpItems(FtpClient client, string dir)
        {
            try
            {
                return client.GetListing(dir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GetFtpItems] Exception listing contents of directory [" + dir + "].");
                Console.Write(ex.StackTrace);
                return null;
            }
        }
    }

    internal class ContentDirectoryGenerator
    {
        private List<string> ContentDirectories = new();

        public void GetContentDirectories(FtpClient ftpClient, string rootDir)
        {
            FtpListItem[] topDirectories = ftpClient.GetListing(rootDir);
            string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ftpContentPaths.txt");

            foreach (var item in topDirectories)
            {
                FtpListItem[] innerDirectories = ftpClient.GetListing(item.FullName);
                foreach (var innerItem in innerDirectories)
                {
                    ContentDirectories.Add(innerItem.FullName);
                    //Console.WriteLine(innerItem.FullName); //Test
                }
            }

            File.AppendAllLines(outputPath, ContentDirectories);
        }
    }
}