using UnityEngine;

namespace KapiVR.Networking
{
    /// <summary>
    /// Tells the Kapi systems where a networked player's head and hands are. Photon VR players are found
    /// automatically, so this is only needed on your own network player prefab if you don't use Photon VR.
    /// Put it on the same object as the PhotonView (or a child).
    /// </summary>
    [AddComponentMenu("Kapi VR/Network Avatar")]
    public sealed class KapiNetworkAvatar : MonoBehaviour
    {
        public Transform head;
        public Transform leftHand;
        public Transform rightHand;
    }
}
