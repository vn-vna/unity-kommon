using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    [CreateAssetMenu(
        fileName = "UserSegmentationConfiguration",
        menuName = "Scheherazade/Integration/Segmentation/User Segmentation Configuration",
        order = 1
    )]
    public class UserSegmentationConfiguration : ScriptableObject
    {
        public SegmentationDeclaration[] Declarations;
    }
}