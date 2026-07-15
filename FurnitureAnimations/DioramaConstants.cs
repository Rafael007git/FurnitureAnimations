using System.Collections.Generic;

namespace FurnitureAnimationsMod
{
    public static class DioramaConstants
    {
        // 1. Общий список всех разрешенных объектов (биологические кости + свет)
        public static readonly HashSet<string> AnatomyBoneRegistry = new HashSet<string>
        {
            // Корневые узлы и таз
            "Player", "Kira", "Genesis8Female", "hip", "pelvis",

            // Интимная анатомия
            "Rectum", "Vagina", "Clitoris", "Colon",
            "Left Small Labia 1", "Left Small Labia 2", "Left Small Labia 3", "Left Small Labia 4",
            "Right Small Labia1", "Right Small Labia 2", "Right Small Labia 3", "Right Small Labia 4",
            "lLabiumMajora1", "lLabiumMajora2", "rLabiumMajora1", "rLabiumMajora2",

            // Позвоночник, торс и грудь
            "abdomenLower", "abdomenUpper", "chestLower", "chestUpper", "lPectoral", "rPectoral",

            // Нижние конечности (Ноги и пальцы ног)
            "lThighBend", "rThighBend", "lThighTwist", "rThighTwist", "lShin", "rShin",
            "lFoot", "rFoot", "lMetatarsals", "rMetatarsals", "lToe", "rToe",
            "lBigToe", "lBigToe_2", "rBigToe", "rBigToe_2",
            "lSmallToe1", "lSmallToe1_2", "rSmallToe1", "rSmallToe1_2",
            "lSmallToe2", "lSmallToe2_2", "rSmallToe2", "rSmallToe2_2",
            "lSmallToe3", "lSmallToe3_2", "rSmallToe3", "rSmallToe3_2",
            "lSmallToe4", "lSmallToe4_2", "rSmallToe4", "rSmallToe4_2",

            // Upper Extremities (Плечи, руки и запястья)
            "lCollar", "rCollar", "lShldrBend", "rShldrBend", "lShldrTwist", "rShldrTwist",
            "lForearmBend", "rForearmBend", "lForearmTwist", "rForearmTwist", "lHand", "rHand",
            "lCarpal1", "lCarpal2", "lCarpal3", "lCarpal4",
            "rCarpal1", "rCarpal2", "rCarpal3", "rCarpal4",

            // Пальцы левой руки
            "lThumb1", "lThumb2", "lThumb3", "lIndex1", "lIndex2", "lIndex3",
            "lMid1", "lMid2", "lMid3", "lRing1", "lRing2", "lRing3", "lPinky1", "lPinky2", "lPinky3",

            // Пальцы правой руки
            "rThumb1", "rThumb2", "rThumb3", "rIndex1", "rIndex2", "rIndex3",
            "rMid1", "rMid2", "rMid3", "rRing1", "rRing2", "rRing3", "rPinky1", "rPinky2", "rPinky3",

            // Шея, голова и мимика лица
            "neckLower", "neckUpper", "head", "lowerJaw", "upperTeeth", "lowerTeeth",
            "lEar", "rEar", "lEye", "rEye", "Nose", "MidNoseBridge", "CenterBrow",
            "upperFaceRig", "lowerFaceRig", "rJawClench", "lJawClench", "BelowJaw", "Chin",
            "rCheekUpper", "lCheekUpper", "rCheekLower", "lCheekLower",
            "rSquintOuter", "rSquintInner", "lSquintOuter", "lSquintInner",
            "rLipUpperOuter", "rLipUpperInner", "lLipUpperInner", "lLipUpperOuter", "LipUpperMiddle",
            "rLipLowerOuter", "rLipLowerInner", "LipLowerMiddle", "lLipLowerInner", "lLipLowerOuter",
            "rLipCorner", "lLipCorner", "LipBelow",
            "rNasolabialMiddle", "lNasolabialMiddle", "rNasolabialUpper", "lNasolabialUpper",
            "rNasolabialLower", "lNasolabialLower", "rLipNasolabialCrease", "lLipNasolabialCrease",
            "rLipBelowNose", "lLipBelowNose", "rNostril", "lNostril",
            "lBrowOuter", "lBrowMid", "lBrowInner", "rBrowOuter", "rBrowMid", "rBrowInner",
            "lEyelidLowerInner", "lEyelidLower", "lEyelidLowerOuter", "lEyelidOuter",
            "lEyelidUpperOuter", "lEyelidUpper", "lEyelidUpperInner", "lEyelidInner",
            "rEyelidLowerInner", "rEyelidLower", "rEyelidLowerOuter", "rEyelidOuter",
            "rEyelidUpperOuter", "rEyelidUpper", "rEyelidUpperInner", "rEyelidInner",

            // Язык
            "tongue01", "tongue02", "tongue03", "tongue04",

            // Источники света
            "Point Light (1)", "Point Light (5)", "Point Light (9)"
        };

        // === ОСОБЫЙ РАЗДЕЛ СПРАВОЧНИКА ===
        // Объекты, которым критически важно возвращать и локальную ПОЗИЦИЮ (смещение), а не только поворот
        public static readonly HashSet<string> PositionalObjectsRegistry = new HashSet<string>
        {
            "hip",
            "Point Light (1)",
            "Point Light (5)",
            "Point Light (9)"
        };
    }
}
