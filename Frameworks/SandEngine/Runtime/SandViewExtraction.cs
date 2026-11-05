#if !PLA_CREATIVE_BUILD
using Unity.Jobs;
#endif

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    public static class SandViewExtraction
    {
        public static void Extract(ISandBackend backend)
        {
#if PLA_CREATIVE_BUILD
            int[] cells = backend.CurrentGrid.GetArrayForStep();
            SandViewCell[] view = backend.ViewBuffer.GetArrayForStep();
            for (int index = 0; index < cells.Length; index++)
                view[index] = new SandViewCell
                {
                    elementType = (byte)(cells[index] & 255),
                    packedColorVariant = (byte)((cells[index] >> 16) & 255)
                };
#else
            new ExtractViewCellsJob
            {
                source = backend.CurrentGrid, dest = backend.ViewBuffer
            }.Schedule(backend.CellCount, 1024).Complete();
#endif
        }
    }
}
