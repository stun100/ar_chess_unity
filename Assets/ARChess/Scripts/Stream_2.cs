using System;
using System.Collections;
using System.Collections.Generic; // Add this line
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using WebSocketSharp;
using UnityEngine.UI;

public class Stream_2 : MonoBehaviour
{
    public ARCameraManager arCameraManager;
    public string backendUrlInput = "192.168.1.5:8000";
    public Text fpsText;
    public Text screenInfoText; // Add this line

    private WebSocket ws;
    private int frameCount = 0;
    private float elapsedTime = 0.0f;
    private List<Rect> boundingBoxes = new List<Rect>(); // Add this line

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ws = new WebSocket($"ws://{backendUrlInput}/stream");
        ws.OnMessage += (sender, e) =>
        {
            Debug.Log("Response: " + e.Data);
            // Parse the response to get the bounding boxes
            var response = JsonUtility.FromJson<ResponseData>(e.Data);
            boundingBoxes.Clear();
            foreach (var box in response.corners)
            {
                boundingBoxes.Add(new Rect(box.x_min, box.y_min, box.x_max - box.x_min, box.y_max - box.y_min));
            }
        };
        ws.OnError += (sender, e) =>
        {
            Debug.Log("Error: " + e.Message);
        };
        ws.Connect();
        StartCoroutine(streamImage());

        // Display screen width and height
        screenInfoText.text = $"Screen Width: {Screen.width}, Screen Height: {Screen.height}"; // Add this line

        // Create a 3D plane in front of the AR camera
        CreatePlaneInFrontOfARCamera();
    }

    // Update is called once per frame
    void Update()
    {
        // Calculate FPS based on the number of images sent
        elapsedTime += Time.deltaTime;
        if (elapsedTime >= 1.0f)
        {
            fpsText.text = $"FPS: {frameCount}";
            frameCount = 0;
            elapsedTime = 0.0f;
        }
        // ...existing code...
    }

    private string accessImage()
    {
        // Acquire an XRCpuImage
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            return null;

        // Set up our conversion params
        var conversionParams = new XRCpuImage.ConversionParams
        {
            // Convert the entire image
            inputRect = new RectInt(0, 0, image.width, image.height),

            // Output at full resolution
            outputDimensions = new Vector2Int(image.width, image.height),

            // Convert to RGBA format
            outputFormat = TextureFormat.RGBA32,

            // Flip across the vertical axis (mirror image)
            transformation = XRCpuImage.Transformation.MirrorY
        };

        // Create a Texture2D to store the converted image
        var texture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);

        // Texture2D allows us write directly to the raw texture data as an optimization
        var rawTextureData = texture.GetRawTextureData<byte>();
        try
        {
            unsafe
            {
                // Synchronously convert to the desired TextureFormat
                image.Convert(
                    conversionParams,
                    new IntPtr(rawTextureData.GetUnsafePtr()),
                    rawTextureData.Length);
            }
        }
        finally
        {
            // Dispose the XRCpuImage after we're finished to prevent any memory leaks
            image.Dispose();
        }

        // Apply the converted pixel data to our texture
        texture.Apply();

        byte[] imageBytes = texture.EncodeToPNG();
        string base64Image = Convert.ToBase64String(imageBytes);

        return base64Image;
    }

    private IEnumerator streamImage()
    {
        while (true)
        {
            string base64Image = accessImage();
            if (base64Image != null)
            {
                // Convert base64 string to byte array
                byte[] imageBytes = Convert.FromBase64String(base64Image);

                // Send image bytes via WebSocket
                ws.Send(imageBytes);
                frameCount++; // Increment frame count for each image sent
            }
            yield return new WaitForSeconds(1f); // Adjust the interval as needed
        }
    }

    void OnDestroy()
    {
        if (ws != null)
        {
            ws.Close();
        }
    }

    void OnGUI()
    {
        // Draw the bounding boxes on the screen
        foreach (var box in boundingBoxes)
        {
            // Convert the bounding box coordinates to screen coordinates
            float screenX = box.x / 304f * Screen.width;
            float screenY = box.y / 640f * Screen.height;
            float screenWidth = box.width / 304f * Screen.width;
            float screenHeight = box.height / 640f * Screen.height;

            // Draw the bounding box
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(screenX, screenY, screenWidth, screenHeight), Texture2D.whiteTexture);
        }
    }

    private void CreatePlaneInFrontOfARCamera()
    {
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Camera arCamera = arCameraManager.GetComponent<Camera>();
        plane.transform.position = arCamera.transform.position + arCamera.transform.forward * 2.0f;
        plane.transform.rotation = Quaternion.Euler(90, 0, 0);
        plane.transform.localScale = new Vector3(0.1f, 1, 0.1f); // Adjust the scale as needed
    }

    [System.Serializable]
    private class ResponseData
    {
        public List<BoundingBox> corners;
    }

    [System.Serializable]
    private class BoundingBox
    {
        public float x_min;
        public float y_min;
        public float x_max;
        public float y_max;
    }
}
