using NUnit.Framework;
using UnityEngine;

namespace TimeTheft.Tests
{
    public sealed class GameViewportTests
    {
        [TestCase(1920, 1080, 0, 0, 1920, 1080)]
        [TestCase(1920, 1200, 0, 60, 1920, 1080)]
        [TestCase(2560, 1080, 320, 0, 1920, 1080)]
        [TestCase(1024, 768, 0, 96, 1024, 576)]
        public void RequestedResolutionsFitWithoutCropping(int width, int height, int x, int y, int w, int h)
        {
            var rect = GameViewport.CalculateRect(width, height);
            Assert.That(rect.x * width, Is.EqualTo(x).Within(.01f));
            Assert.That(rect.y * height, Is.EqualTo(y).Within(.01f));
            Assert.That(rect.width * width, Is.EqualTo(w).Within(.01f));
            Assert.That(rect.height * height, Is.EqualTo(h).Within(.01f));
            Assert.That(rect.width * width / (rect.height * height), Is.EqualTo(16f / 9).Within(.0001f));
        }
    }
}
