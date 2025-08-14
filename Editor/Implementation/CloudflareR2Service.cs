using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using UnityEngine;

namespace Addressables_Wrapper.Editor.CDN
{
    /// <summary>
    /// Service class to handle Cloudflare R2 operations using S3-compatible APIs.
    /// </summary>
    public class CloudflareR2Service : IDisposable
    {
        // S3 configuration fields
        private readonly string accountID;
        private readonly string accessKey; 
        private readonly string secretKey;
        private readonly string bucketName;
        private readonly AmazonS3Client s3Client;

        /// <summary>
        /// Initializes the Cloudflare R2 Service with S3-compatible credentials and extended timeouts for CI/CD.
        /// </summary>
        /// <param name="accountID">Cloudflare Account ID</param>
        /// <param name="accessKey">API token from Cloudflare R2 (to be used as the Access Key)</param>
        /// <param name="secretKey">Cloudflare R2 Secret Key</param>
        /// <param name="requestTimeout">Optional timeout for requests in minutes (default: 10)</param>
        public CloudflareR2Service(string accountID, string accessKey, string secretKey, int requestTimeout = 10)
        {
            this.accountID = accountID;
            this.accessKey = accessKey;
            this.secretKey = secretKey;

            //Debug.Log("Initializing Cloudflare R2 Service...");
            //Debug.Log($"Account ID: {this.accountID}");
            //Debug.Log($"Access Key: {this.accessKey}");
            //Debug.Log($"Secret Key: {this.secretKey}");
            //Debug.Log($"Request Timeout: {requestTimeout} minutes");

            // Create S3 configuration specifically for R2 with extended timeouts
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{this.accountID}.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                UseHttp = false,
                Timeout = TimeSpan.FromMinutes(requestTimeout),
                ReadWriteTimeout = TimeSpan.FromMinutes(requestTimeout),
                MaxErrorRetry = 5,  // Increase retry attempts
                ThrottleRetries = false // Don't throttle retries 
            };

            //Debug.Log($"Using Service URL: {s3Config.ServiceURL}");
            //Debug.Log($"S3 Config - Timeout: {s3Config.Timeout}, ReadWriteTimeout: {s3Config.ReadWriteTimeout}, " +
                      //$"MaxErrorRetry: {s3Config.MaxErrorRetry}, ThrottleRetries: {s3Config.ThrottleRetries}");

            try
            {
                //Debug.Log("Creating S3 Client...");
                var credentials = new BasicAWSCredentials(accessKey, secretKey);
                s3Client = new AmazonS3Client(credentials, s3Config);
                //Debug.Log("S3 Client successfully initialized.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error initializing S3 Client: {e.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Uploads all files from a local directory to the specified Cloudflare R2 bucket.
        /// </summary>
        public async Task UploadDirectory(
            string buildPath,
            string bucketName,
            string remotePathPrefix,
            Action<float, string> progressCallback)
        {
            try 
            {
                Debug.Log($"Starting directory upload from {buildPath} to bucket {bucketName} with prefix {remotePathPrefix}");
                var files = Directory.GetFiles(buildPath, "*.*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                int completedFiles = 0;

                Debug.Log($"Found {totalFiles} files to upload");

                foreach (string filePath in files)
                {
                    string relativePath = filePath.Substring(buildPath.Length).TrimStart('\\', '/');
                    string key = Path.Combine(remotePathPrefix, relativePath).Replace("\\", "/");

                    try
                    {
                        float progress = (float)completedFiles / totalFiles;
                        progressCallback?.Invoke(progress, relativePath);

                        Debug.Log($"Uploading file {completedFiles+1}/{totalFiles}: {relativePath} to {key}");

                        // Simple direct upload without streaming
                        using (var fileStream = File.OpenRead(filePath))
                        {
                            var putRequest = new PutObjectRequest
                            {
                                BucketName = bucketName,
                                Key = key,
                                InputStream = fileStream,
                                ContentType = GetContentType(Path.GetExtension(filePath)),
                                AutoCloseStream = true,
                                UseChunkEncoding = false
                            };

                            // Use the synchronous method in batch mode to ensure we don't proceed until the upload completes
                            var response = await s3Client.PutObjectAsync(putRequest);
                            Debug.Log($"Upload response: RequestId={response.ResponseMetadata.RequestId}, HttpStatus={response.HttpStatusCode}");
                        }

                        completedFiles++;
                        Debug.Log($"Successfully uploaded {completedFiles}/{totalFiles}: {key}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to upload {key}: {ex.Message}");
                        Debug.LogException(ex);
                        throw;
                    }
                }

                progressCallback?.Invoke(1.0f, "Complete");
                Debug.Log($"Upload directory completed. All {completedFiles}/{totalFiles} files uploaded successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during directory upload: {ex.Message}");
                Debug.LogException(ex);
                throw;
            }
        }

        /// <summary>
        /// Tests the connection to Cloudflare R2 by listing available buckets.
        /// </summary>
        public async Task<List<string>> TestConnection()
        {
            try
            {
                Debug.Log($"Attempting to connect to: {s3Client.Config.ServiceURL}");

                if (s3Client == null)
                {
                    Debug.LogError("Error: s3Client is null! Did initialization fail?");
                    throw new NullReferenceException("s3Client is not initialized.");
                }

                var response = await s3Client.ListBucketsAsync();
                List<string> buckets = new List<string>();

                foreach (var bucket in response.Buckets)
                {
                    buckets.Add(bucket.BucketName);
                }

                return buckets;
            }
            catch (AmazonS3Exception s3Ex)
            {
                Debug.LogError($"S3 Error Code: {s3Ex.ErrorCode}, Status: {s3Ex.StatusCode}");
                Debug.LogError($"Request ID: {s3Ex.RequestId}, Service: {s3Ex.ErrorCode}");
                Debug.LogError($"Detailed R2 Error: {s3Ex.Message}");
                if (s3Ex.InnerException != null)
                {
                    Debug.LogError($"Inner Exception: {s3Ex.InnerException.Message}");
                }
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"General Error: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Debug.LogError($"Inner Exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }        

        /// <summary>
        /// Copies objects from one bucket to another.
        /// </summary>
        public async Task CopyObjects(string sourceBucket, string targetBucket, string sourcePrefix, string targetPrefix, Action<float, string> progressCallback)
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = sourceBucket,
                Prefix = sourcePrefix
            };

            ListObjectsV2Response response;
            int totalObjects = 0;
            int copiedObjects = 0;

            do
            {
                response = await s3Client.ListObjectsV2Async(listRequest);
                totalObjects += response.S3Objects.Count;

                foreach (var obj in response.S3Objects)
                {
                    try
                    {
                        string targetKey = obj.Key;
                        if (!string.IsNullOrEmpty(sourcePrefix) && !string.IsNullOrEmpty(targetPrefix))
                        {
                            targetKey = obj.Key.Replace(sourcePrefix, targetPrefix);
                        }

                        var copyRequest = new CopyObjectRequest
                        {
                            SourceBucket = sourceBucket,
                            SourceKey = obj.Key,
                            DestinationBucket = targetBucket,
                            DestinationKey = targetKey,
                            CannedACL = S3CannedACL.PublicRead
                        };

                        await s3Client.CopyObjectAsync(copyRequest);
                        copiedObjects++;
                        float progress = (float)copiedObjects / totalObjects;
                        progressCallback?.Invoke(progress, obj.Key);
                        Debug.Log($"Copied: {obj.Key} to {targetKey}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to copy {obj.Key}: {ex.Message}");
                        throw;
                    }
                }

                listRequest.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated);
        }

        /// <summary>
        /// Retrieves the latest version (by date or lexicographical order) in the bucket using a given prefix.
        /// </summary>
        public async Task<string> GetLatestVersion(string prefix = "v")
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Delimiter = "/"
            };

            var response = await s3Client.ListObjectsV2Async(listRequest);
            string latestVersion = null;
            DateTime latestDate = DateTime.MinValue;

            foreach (var commonPrefix in response.CommonPrefixes)
            {
                string version = commonPrefix.TrimEnd('/');
                if (version.StartsWith(prefix))
                {
                    if (version.Length > 1 && version.Contains("_"))
                    {
                        try
                        {
                            string dateStr = version.Substring(1, version.IndexOf('_') - 1);
                            string timeStr = version.Substring(version.IndexOf('_') + 1);
                            if (DateTime.TryParseExact($"{dateStr}_{timeStr}", "yyyyMMdd_HHmmss",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out DateTime versionDate))
                            {
                                if (versionDate > latestDate)
                                {
                                    latestDate = versionDate;
                                    latestVersion = version;
                                }
                            }
                        }
                        catch
                        {
                            if (string.Compare(version, latestVersion) > 0)
                            {
                                latestVersion = version;
                            }
                        }
                    }
                    else
                    {
                        if (string.Compare(version, latestVersion) > 0)
                        {
                            latestVersion = version;
                        }
                    }
                }
            }

            return latestVersion;
        }
        
        /// <summary>
        /// Deletes all objects in the specified bucket, optionally limited to a given prefix.
        /// </summary>
        /// <param name="bucketName">Name of the bucket to delete files from</param>
        /// <param name="prefix">Optional prefix to limit deletion to specific folder/path</param>
        /// <param name="progressCallback">Callback to report progress</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public async Task DeleteAllBucketContents(string bucketName, string prefix = null, Action<float, string> progressCallback = null)
        {
            try
            {
                Debug.Log($"Starting deletion of all objects in bucket '{bucketName}'" + (string.IsNullOrEmpty(prefix) ? "" : $" with prefix '{prefix}'"));

                var listRequest = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = prefix
                };

                ListObjectsV2Response response;
                int totalObjects = 0;
                List<string> objectKeys = new List<string>();

                // First, list all objects to be deleted
                do
                {
                    response = await s3Client.ListObjectsV2Async(listRequest);
                    totalObjects += response.S3Objects.Count;
                    
                    foreach (var obj in response.S3Objects)
                    {
                        objectKeys.Add(obj.Key);
                    }
                    
                    listRequest.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated);

                Debug.Log($"Found {totalObjects} objects to delete");
                
                if (totalObjects == 0)
                {
                    progressCallback?.Invoke(1.0f, "No objects to delete");
                    return;
                }

                int deletedObjects = 0;
                
                // Delete objects in batches of 1000 (S3 API limit)
                for (int i = 0; i < objectKeys.Count; i += 1000)
                {
                    var batch = objectKeys.Skip(i).Take(1000).ToList();
                    
                    var deleteRequest = new DeleteObjectsRequest
                    {
                        BucketName = bucketName,
                        Objects = batch.Select(key => new KeyVersion { Key = key }).ToList(),
                        Quiet = true // Minimize response size
                    };
                    
                    var deleteResponse = await s3Client.DeleteObjectsAsync(deleteRequest);
                    deletedObjects += batch.Count;
                    
                    float progress = (float)deletedObjects / totalObjects;
                    progressCallback?.Invoke(progress, $"Deleted {deletedObjects}/{totalObjects} objects");
                    
                    Debug.Log($"Deleted batch of {batch.Count} objects. Progress: {progress:P2}");
                }
                
                progressCallback?.Invoke(1.0f, $"Successfully deleted {deletedObjects} objects");
                Debug.Log($"Successfully deleted all {deletedObjects} objects from bucket '{bucketName}'");
            }
            catch (AmazonS3Exception s3Ex)
            {
                Debug.LogError($"S3 Error during deletion: {s3Ex.ErrorCode}, Status: {s3Ex.StatusCode}");
                Debug.LogError($"Detailed Error: {s3Ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during bucket content deletion: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Cleans up incomplete multipart uploads in the specified bucket.
        /// </summary>
        /// <param name="bucketName">Name of the bucket to clean</param>
        /// <param name="prefix">Optional prefix to limit cleanup to specific folder/path</param>
        /// <param name="progressCallback">Callback to report progress</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public async Task CleanupMultipartUploads(string bucketName, string prefix = null, Action<float, string> progressCallback = null)
        {
            try
            {
                Debug.Log($"Starting cleanup of incomplete multipart uploads in bucket '{bucketName}'" +
                         (string.IsNullOrEmpty(prefix) ? "" : $" with prefix '{prefix}'"));

                // List all in-progress multipart uploads
                var listRequest = new ListMultipartUploadsRequest
                {
                    BucketName = bucketName,
                    Prefix = prefix
                };

                ListMultipartUploadsResponse response;
                int totalUploads = 0;
                int cleanedUploads = 0;
                List<MultipartUpload> uploads = new List<MultipartUpload>();

                // First, list all multipart uploads
                do
                {
                    response = await s3Client.ListMultipartUploadsAsync(listRequest);
                    uploads.AddRange(response.MultipartUploads);
                    
                    // Set up for next page if needed
                    listRequest.KeyMarker = response.NextKeyMarker;
                    listRequest.UploadIdMarker = response.NextUploadIdMarker;
                    
                } while (response.IsTruncated);

                totalUploads = uploads.Count;
                
                Debug.Log($"Found {totalUploads} incomplete multipart uploads to clean up");
                
                if (totalUploads == 0)
                {
                    progressCallback?.Invoke(1.0f, "No multipart uploads to clean up");
                    return;
                }

                // Abort each multipart upload
                foreach (var upload in uploads)
                {
                    try
                    {
                        var abortRequest = new AbortMultipartUploadRequest
                        {
                            BucketName = bucketName,
                            Key = upload.Key,
                            UploadId = upload.UploadId
                        };
                        
                        await s3Client.AbortMultipartUploadAsync(abortRequest);
                        cleanedUploads++;
                        
                        float progress = (float)cleanedUploads / totalUploads;
                        progressCallback?.Invoke(progress, $"Cleaned {cleanedUploads}/{totalUploads} uploads");
                        
                        Debug.Log($"Aborted multipart upload: {upload.Key} (UploadId: {upload.UploadId})");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to abort multipart upload {upload.Key}: {ex.Message}");
                        // Continue with the next upload instead of throwing
                    }
                }
                
                progressCallback?.Invoke(1.0f, $"Successfully cleaned up {cleanedUploads} multipart uploads");
                Debug.Log($"Successfully cleaned up {cleanedUploads} multipart uploads from bucket '{bucketName}'");
            }
            catch (AmazonS3Exception s3Ex)
            {
                Debug.LogError($"S3 Error during multipart cleanup: {s3Ex.ErrorCode}, Status: {s3Ex.StatusCode}");
                Debug.LogError($"Detailed Error: {s3Ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during multipart upload cleanup: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Gets the internal S3 client for advanced configuration.
        /// </summary>
        /// <returns>The Amazon S3 client instance</returns>
        public AmazonS3Client GetS3Client()
        {
            return s3Client;
        }

        /// <summary>
        /// Disposes of the S3 client.
        /// </summary>
        public void Dispose()
        {
            s3Client?.Dispose();
        }

        /// <summary>
        /// Helper method to determine content type based on file extension.
        /// </summary>
        private string GetContentType(string extension)
        {
            switch (extension.ToLower())
            {
                case ".json": return "application/json";
                case ".bundle": return "application/octet-stream";
                case ".hash":
                case ".manifest": return "text/plain";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".txt": return "text/plain";
                case ".html": return "text/html";
                case ".css": return "text/css";
                case ".js": return "application/javascript";
                default: return "application/octet-stream";
            }
        }
    }
}
