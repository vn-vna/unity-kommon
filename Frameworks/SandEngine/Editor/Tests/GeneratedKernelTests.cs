using NUnit.Framework;

namespace Com.Scheherazade.Common.Frameworks.SandEngine.Tests
{
    public sealed class GeneratedKernelTests
    {
        [Test]
        public void KernelsMatchAuthoritativeSourcesAtTheirNewLocation()
        {
            Editor.SandPlaKernelGenerator.ValidateCurrent();
            Assert.That(typeof(Pla.SandPlaStep).GetField("current").FieldType, Is.EqualTo(typeof(int[])));
            Assert.That(typeof(Pla.SandPlaRegionalStep).GetField("gravity").FieldType, Is.EqualTo(typeof(byte[])));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        [TestCase(unchecked((int)0xA1B2C301))]
        public void PackedCellGettersPreserveUnsignedMaskResults(int cell)
        {
            Assert.That((byte)SandCell.GetElementType(cell), Is.EqualTo((byte)(cell & 0xFFL)));
            Assert.That(SandCell.GetVelocity(cell), Is.EqualTo((int)((cell & 0xFF00L) >> 8)));
            Assert.That(SandCell.GetColorByte(cell), Is.EqualTo((int)((cell & 0xFF0000L) >> 16)));
            Assert.That(SandCell.GetPayload(cell), Is.EqualTo((int)((cell & 0xFFFF0000L) >> 16)));
        }
    }
}
