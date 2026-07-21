using UnityEngine;

namespace Extra.TelemetryLog
{
    public static class FrustumUtils
    {
        private static Camera _cachedCamera;
        private static Plane[] _cachedPlanes;
        private static int _cachedFrameCount = -1;

        /// <summary>
        /// Recalculates and caches the camera's frustum planes.
        /// Caches planes per frame to avoid redundant calculations when querying multiple entities.
        /// </summary>
        public static Plane[] GetFrustumPlanes(Camera camera)
        {
            if (camera == null) return null;

            if (_cachedCamera == camera && _cachedFrameCount == Time.frameCount && _cachedPlanes != null)
            {
                return _cachedPlanes;
            }

            _cachedCamera = camera;
            _cachedFrameCount = Time.frameCount;
            _cachedPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            return _cachedPlanes;
        }

        /// <summary>
        /// Returns true if any part of the renderer's bounds is inside the camera's frustum.
        /// </summary>
        public static bool IsVisibleToCamera(Renderer renderer, Camera camera)
        {
            if (renderer == null || camera == null) return false;

            Plane[] planes = GetFrustumPlanes(camera);
            if (planes == null) return false;

            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }

        /// <summary>
        /// Point-based visibility check using Camera.WorldToViewportPoint.
        /// Returns true if the transform's position falls within [0,1] viewport range and is in front of the camera.
        /// </summary>
        public static bool IsVisibleToCamera(Transform transform, Camera camera)
        {
            if (transform == null || camera == null) return false;

            Vector3 viewportPoint = camera.WorldToViewportPoint(transform.position);
            return viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                   viewportPoint.y >= 0f && viewportPoint.y <= 1f &&
                   viewportPoint.z > 0f;
        }
    }
}
