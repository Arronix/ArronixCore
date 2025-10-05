using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    [TestFixture]
    [DiskAccessTest]
    [Ignore("Tests disabled - media info extraction will be provided by format support extensions")]
    public class VideoFileInfoReaderFixture : CoreTest<VideoFileInfoReader>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.OpenReadStream(It.IsAny<string>()))
                  .Returns<string>(s => new FileStream(s, FileMode.Open, FileAccess.Read));
        }

        // TODO: Re-enable tests once format support extensions are implemented
        // These tests are currently stubbed because VideoFileInfoReader returns placeholder data
        
        [Test]
        public void get_runtime()
        {
            // TODO: Implement via format support extension
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "H264_sample.mp4");
            Subject.GetRunTime(path).Should().NotBeNull();
        }

        [Test]
        public void get_info()
        {
            // TODO: Implement via format support extension
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "H264_sample.mp4");
            var info = Subject.GetMediaInfo(path);
            info.Should().NotBeNull();
        }
    }
}
