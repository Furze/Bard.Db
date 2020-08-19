using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Newtonsoft.Json;

namespace Bard.Db
{
    /// <summary>
    /// BardDb Db class to provide base functionality (Internal Use)
    /// </summary>
    public abstract class BardDbBase
    {
        private protected readonly string DatabaseName;
        private protected readonly string PortNumber;
        private protected readonly string ImageName;
        private protected readonly string TagName;
        private protected readonly DockerClient DockerClient;
        private string _dbContainerId = null!;

        internal BardDbBase(string databaseName, string portNumber, string imageName, string tagName)
        {
            DatabaseName = databaseName;
            PortNumber = portNumber;
            ImageName = imageName;
            TagName = tagName;
            DockerClient = new DockerClientConfiguration().CreateClient();
        }

        /// <summary>
        /// Start the database
        /// <remarks>Will download the image and create the container if it does not exist already.</remarks>
        /// </summary>
        /// <returns>The IP address of the host machine.</returns>
        public string StartDatabase()
        {
            var result = AsyncHelper.RunSync(StartDatabaseAsync);
        
            return result;
        }
        
        /// <summary>
        /// Stops the database
        /// <remarks>Will not remove any docker containers or images</remarks>
        /// </summary>
        /// <returns>True if successful</returns>
        public bool StopDatabase()
        {
            return AsyncHelper.RunSync(StopDatabaseAsync);
        }
        
        /// <summary>
        /// Stops the database Async
        /// <remarks>Will not remove any docker containers or images</remarks>
        /// </summary>
        /// <returns>True if successful</returns>
        public Task<bool> StopDatabaseAsync()
        {
            Console.WriteLine($"Starting Container {ImageName}:{TagName} - {_dbContainerId}");

            return DockerClient.Containers.StopContainerAsync(_dbContainerId, new ContainerStopParameters());
        }
        
        /// <summary>
        /// Start the database Async
        /// <remarks>Will download the image and create the container if it does not exist already.</remarks>
        /// </summary>
        /// <returns>The IP address of the host machine.</returns>
        public async Task<string> StartDatabaseAsync()
        {
            await PullImageIfRequired();

            _dbContainerId = await CreateContainerIfRequired();

            return await StartContainer(_dbContainerId);
        }

        private protected abstract Task<string> CreateContainerIfRequired();

        private async Task PullImageIfRequired()
        {
            var dataBaseImages = await DockerClient.Images.ListImagesAsync(new ImagesListParameters
            {
                MatchName = $"{ImageName}:{TagName}"
            });

            if (dataBaseImages.Any() == false)
            {
                Console.WriteLine($"Pulling Image {ImageName}:{TagName}");

                await DockerClient.Images.CreateImageAsync(new ImagesCreateParameters
                    {
                        FromImage = ImageName,
                        Tag = TagName
                    },
                    null, new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(message.Status);
                    }));
            }
        }
        
        private protected async Task<ContainerListResponse?> RetrieveContainer()
        {
            var fullImage = $"{ImageName}:{TagName}";
            
            var containers = await DockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    {"name", new Dictionary<string, bool> {{DatabaseName, true}}}
                }
            });

            var matchingContainer = containers.FirstOrDefault(response => response.Image != fullImage);
            
            if (matchingContainer != null)
            {
                ThrowDuplicateContainerException(fullImage, matchingContainer);
            }
            
            var testDb = containers.SingleOrDefault(response => response.Image == fullImage);

            return testDb;
        }

        private void ThrowDuplicateContainerException(string fullImage, ContainerListResponse matchingContainers)
        {
            throw new BardException($"Unable to start Container with name: {DatabaseName} for image {fullImage}" +
                                    $"because a container already exists with that name for a different image." +
                                    $"{Environment.NewLine}{Environment.NewLine}" +
                                    $"Either rename the database for this image or delete the old container." +
                                    $"{Environment.NewLine}{Environment.NewLine}" +
                                    $"Run this command from the command line to view the offending container" +
                                    $"{Environment.NewLine}{Environment.NewLine}" +
                                    $"docker ps -a -f name={DatabaseName}" +
                                    $"{Environment.NewLine}{Environment.NewLine}" +
                                    $"And this command to remove it." +
                                    $"{Environment.NewLine}{Environment.NewLine}" +
                                    $"docker rm {matchingContainers.ID}" +
                                    $"{Environment.NewLine}"
            );
        }

        private async Task<string> StartContainer(string dbContainerId)
        {
            Console.WriteLine($"Starting Container {ImageName}:{TagName} - {dbContainerId}");

            await DockerClient.Containers.StartContainerAsync(dbContainerId, new ContainerStartParameters());

            var container = await DockerClient.Containers.InspectContainerAsync(dbContainerId);
            
            while (container.State.Health.Status == "starting")
            {
                Console.WriteLine($"Container Status {container.State.Health.Status}");

                if (container.State.Health.Status == "unhealthy")
                    throw new Exception("There is a problem with the container it is unhealthy");

                await Task.Delay(2000);

                container = await DockerClient.Containers.InspectContainerAsync(dbContainerId);
            }
            
            Console.WriteLine($"Container Started {container.State.Health.Status}");
            Console.WriteLine($"Container Started {JsonConvert.SerializeObject(container, Formatting.Indented)}");

            var hostIpAddress = container.NetworkSettings.IPAddress;

            return hostIpAddress;
        }
    }
}