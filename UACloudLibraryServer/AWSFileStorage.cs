﻿/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace UACloudLibrary
{
    using Amazon;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.S3.Util;
    using Amazon.SecurityToken;
    using Amazon.SecurityToken.Model;
    using System;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UACloudLibrary.Interfaces;

    /// <summary>
    /// AWS S3 storage class
    /// </summary>
    public class AWSFileStorage : IFileStorage
    {
        private readonly string _bucket;
        private readonly string _prefix;
        private readonly RegionEndpoint _region;
        /// <summary>
        /// Default constructor
        /// </summary>
        public AWSFileStorage()
        {
            var connStr = Environment.GetEnvironmentVariable("BlobStorageConnectionString");
            if (connStr != null)
            {
                try
                {
                    var uri = new AmazonS3Uri(connStr);

                    _bucket = uri.Bucket;
                    _prefix = uri.Key;
                    if (uri.Region != null) _region = uri.Region;
                }
                catch (Exception ex1)
                {
                    Console.WriteLine($"{connStr} is not a valid S3 Url: {ex1.Message}");
                }
            }

            if (_region == null)
            {
                var regionName = Environment.GetEnvironmentVariable("AWS_REGION");
                if (!string.IsNullOrEmpty(regionName))
                {
                    try
                    {
                        _region = RegionEndpoint.GetBySystemName(regionName);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"{regionName} is not a valid AWS region");
                    }
                }
            }
        }

        private async Task<AmazonS3Client> ConnectToS3(CancellationToken cancellationToken)
        {
            var cred = await GetTemporaryCredentialsAsync(cancellationToken).ConfigureAwait(false);
            var config = _region == null ? new AmazonS3Config() : new AmazonS3Config { RegionEndpoint = _region };

            return new AmazonS3Client(cred, config);

        }

        private static async Task<AWSCredentials> GetTemporaryCredentialsAsync(CancellationToken cancellationToken)
        {
            Credentials credentials = null;

            var roleArn = Environment.GetEnvironmentVariable("AWSRoleArn");
            if (string.IsNullOrEmpty(roleArn))
            {
                return FallbackCredentialsFactory.GetCredentials();
            }

            using (var stsClient = new AmazonSecurityTokenServiceClient())
            {
                var request = new AssumeRoleRequest
                {
                    RoleArn = roleArn,
                    DurationSeconds = 1200,
                    RoleSessionName = "S3AccessRole"
                };

                var response = await stsClient.AssumeRoleAsync(request, cancellationToken).ConfigureAwait(false);
                credentials = response.Credentials;
            }

            var sessionCredentials =
                        new SessionAWSCredentials(credentials.AccessKeyId,
                                                  credentials.SecretAccessKey,
                                                  credentials.SessionToken);
            return sessionCredentials;

        }
        /// <summary>
        /// Find a file based on a unique name
        /// </summary>
        public async Task<string> FindFileAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var s3Client = await ConnectToS3(cancellationToken).ConfigureAwait(false))
                {
                    var key = string.IsNullOrEmpty(_prefix) ? name : _prefix + name;

                    await s3Client.GetObjectMetadataAsync(_bucket, key, cancellationToken).ConfigureAwait(false);

                }

                return name;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        /// <summary>
        /// Upload a file to a blob and return a handle to the file that can be stored in the index database
        /// </summary>
        public async Task<string> UploadFileAsync(string name, string content, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var s3 = await ConnectToS3(cancellationToken).ConfigureAwait(false))
                {
                    var key = string.IsNullOrEmpty(_prefix) ? name : _prefix + name;

                    var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));

                    var putRequest = new PutObjectRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        InputStream = ms
                    };

                    var response = await s3.PutObjectAsync(putRequest, cancellationToken).ConfigureAwait(false);
                    if (response.HttpStatusCode == HttpStatusCode.OK)
                    {
                        return name;
                    }
                    else
                    {
                        Console.WriteLine($"File upload failed!");
                        return string.Empty;
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Download a blob to a file.
        /// </summary>
        public async Task<string> DownloadFileAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var s3 = await ConnectToS3(cancellationToken).ConfigureAwait(false))
                {
                    var key = string.IsNullOrEmpty(_prefix) ? name : _prefix + name;

                    var req = new GetObjectRequest
                    {
                        BucketName = _bucket,
                        Key = key
                    };

                    var res = await s3.GetObjectAsync(req, cancellationToken).ConfigureAwait(false);

                    using (var reader = new StreamReader(res.ResponseStream))
                    {
                        return reader.ReadToEnd();
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return string.Empty;
            }
        }
    }
}
