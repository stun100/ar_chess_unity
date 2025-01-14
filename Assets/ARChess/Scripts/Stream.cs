using System;
using System.Collections;
using System.Collections.Generic; // Add this line
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using WebSocketSharp;
using UnityEngine.UI;
using UnityEditor;

public class Stream : MonoBehaviour
{
    public ARCameraManager arCameraManager;
    public string backendUrlInput = "192.168.1.15:8000";
    public Text fpsText;
    public Text screenInfoText; // Add this line
    public ARPlaneManager planeManager; // Add this line
    public ARRaycastManager raycastManager; // Add this line
    public Button calibrationButton; // Add this line
    public Slider xSlider; // Add this line
    public Slider ySlider; // Add this line
    public Slider rotationSlider; // Add this line
    public Slider scaleSlider; // Add this line
    public Slider zSlider; // Add this line
    private bool isCalibrationMode = false; // Add this line
    public float realScreenWidth = 296f; // Add this line
    public float realScreenHeight = 640f; // Add this line
    public Button toggleBoardButton; // Add this line
    private MeshRenderer boardRenderer; // Add this line
    private bool isBoardVisible = true; // Add this line
    private WebSocket ws;
    private int frameCount = 0;
    private float elapsedTime = 0.0f;
    private List<Vector2> points = new List<Vector2>(); // Change this line
    private GameObject chessboard; // Add this line
    private const int gridSize = 8; // Add this line
    private float cellSize; // Add this line
    private List<GameObject> placedCubes = new List<GameObject>(); // Add this line
    private float xOffset = 0f; // Add this line
    private float yOffset = 0f; // Add this line
    private float zOffset = 0f; // Add this line
    private float yRotation = 0f; // Add this line
    private float scale = 1f; // Add this line
    private Vector3 initialPosition; // Add this line
    private Quaternion initialRotation; // Add this line
    private string lastFromCell = ""; // Add this line
    private string lastToCell = ""; // Add this line

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ws = new WebSocket($"ws://{backendUrlInput}/stream");
        ws.OnMessage += (sender, e) =>
        {
            Debug.Log("Response: " + e.Data);
            // Parse the response to get the points
            var response = JsonUtility.FromJson<ResponseData>(e.Data);
            points.Clear();
            foreach (var point in response.corners)
            {
                points.Add(new Vector2(point.center_x, point.center_y));
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
        planeManager = FindFirstObjectByType<ARPlaneManager>(); // Add this line
        raycastManager = FindFirstObjectByType<ARRaycastManager>(); // Add this line
        chessboard = GameObject.CreatePrimitive(PrimitiveType.Cube); // Add this line
        chessboard.SetActive(false); // Add this line

        calibrationButton.onClick.AddListener(ToggleCalibrationMode); // Add this line
        xSlider.onValueChanged.AddListener(OnXSliderChanged); // Add this line
        ySlider.onValueChanged.AddListener(OnYSliderChanged); // Add this line
        rotationSlider.onValueChanged.AddListener(OnRotationSliderChanged); // Add this line
        scaleSlider.onValueChanged.AddListener(OnScaleSliderChanged); // Add this line
        toggleBoardButton.onClick.AddListener(ToggleBoard); // Add this line
        zSlider.onValueChanged.AddListener(OnZSliderChanged); // Add this line
        boardRenderer = chessboard.GetComponent<MeshRenderer>(); // Add this line
        UpdateScreenInfoText(); // Add this line
    }

    // Update is called once per frame
    void Update()
    {
        // Calculate FPS based on the number of images sent
        elapsedTime += Time.deltaTime;
        if (elapsedTime >= 1.0f)
        {
            fpsText.text = $"{frameCount} img/s";
            frameCount = 0;
            elapsedTime = 0.0f;
        }
        if (isCalibrationMode && planeManager.trackables.count > 0 && points.Count == 4) // Modify this line
        {
            PlaceChessboard();
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
            if (isCalibrationMode) // Add this line
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
            }
            yield return new WaitForSeconds(0.5f); // Adjust the interval as needed
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
        int pointSize = 20;
        // Draw the points on the screen
        if (isCalibrationMode) // Modify this line
        {
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                // Convert the point coordinates to screen coordinates
                float screenX = point.x / realScreenWidth * Screen.width;
                float screenY = point.y / realScreenHeight * Screen.height;
                GUI.color = Color.red;
                GUI.DrawTexture(new Rect(screenX - 5, screenY - 5, pointSize, pointSize), Texture2D.whiteTexture);
            }
            DisplayCorners(); // Add this line to display the corners with different colors
        }
    }

    private void ToggleCalibrationMode() // Modify this method
    {
        isCalibrationMode = !isCalibrationMode;
        UpdateScreenInfoText();
    }

    private void OnXSliderChanged(float value) // Modify this method
    {
        xOffset = value;
        UpdateChessboardTransform();
        UpdateAllColoredCells();
    }

    private void OnYSliderChanged(float value) // Modify this method
    {
        yOffset = value;
        UpdateChessboardTransform();
        UpdateAllColoredCells();
    }

    private void OnRotationSliderChanged(float value) // Modify this method
    {
        yRotation = value;
        UpdateChessboardTransform();
        UpdateAllColoredCells();
    }

    private void OnScaleSliderChanged(float value) // Modify this method
    {
        scale = value;
        UpdateChessboardTransform();
        
        // Recalculate cell size
        cellSize = chessboard.transform.localScale.x / gridSize;
        
        // Recreate colored cells if they exist
        UpdateAllColoredCells();
    }

    private void OnZSliderChanged(float value) // Add this method
    {
        zOffset = value;
        UpdateChessboardTransform();
        UpdateAllColoredCells();
    }

    private void UpdateChessboardTransform() // Modify this method
    {
        if (chessboard != null)
        {
            chessboard.transform.position = initialPosition + new Vector3(xOffset, zOffset, yOffset);
            chessboard.transform.rotation = initialRotation * Quaternion.Euler(0, yRotation, 0);
            chessboard.transform.localScale = new Vector3(scale, 0.01f, scale);
        }
    }

    private void UpdateScreenInfoText() // Add this method
    {
        screenInfoText.text = $"Screen Width: {Screen.width}, Screen Height: {Screen.height}\n" +
                              $"Mode: {(isCalibrationMode ? "Calibration" : "Normal")}";
    }

    private void PlaceChessboard() // Modify this block
    {
        if (!isCalibrationMode) return;

        var worldPoints = new List<Vector3>();
        foreach (var p in points)
        {
            var screenPos = new Vector2(p.x / realScreenWidth * Screen.width,
                                        p.y / realScreenHeight * Screen.height);
            var hits = new List<ARRaycastHit>();
            if (raycastManager.Raycast(screenPos, hits, TrackableType.Planes))
            {
                worldPoints.Add(hits[0].pose.position);

                // Draw the raycast line
                Debug.DrawLine(Camera.main.transform.position, hits[0].pose.position, Color.green, 2f);
            }
        }
        if (worldPoints.Count == 4)
        {
            // Make sure a1 is bottom-left
            // bottom-left = worldPoints[0], bottom-right = worldPoints[1]
            // top-left = worldPoints[2], top-right = worldPoints[3]
            // Adjust indices if needed
            // Example:
            var bottomLeft = worldPoints[0];
            var bottomRight = worldPoints[1];
            var topLeft = worldPoints[2];
            var topRight = worldPoints[3];

            // Board forward = topLeft - bottomLeft
            Vector3 forward = (topLeft - bottomLeft).normalized;
            // Board right = bottomRight - bottomLeft
            Vector3 right = (bottomRight - bottomLeft).normalized;
            // Board up
            Vector3 up = Vector3.Cross(right, forward).normalized;

            // New center
            Vector3 center = (bottomLeft + bottomRight + topLeft + topRight) / 4f;
            // Lift the center a bit to avoid clipping
            center.y += 0.01f;

            // Calculate rotation
            Quaternion rotation = Quaternion.LookRotation(forward, up);

            // Calculate the maximum size
            float maxSize = Mathf.Max(Vector3.Distance(bottomLeft, bottomRight), Vector3.Distance(bottomLeft, topLeft));

            // Place the chessboard
            if (chessboard == null)
            {
                chessboard = new GameObject("Chessboard");
                // Add necessary components to the chessboard
            }
            chessboard.transform.position = center;
            chessboard.transform.rotation = rotation;
            chessboard.transform.localScale = new Vector3(maxSize, 0.01f, maxSize);
            chessboard.SetActive(true);

            // Flip the board by 180 degrees around the Y-axis
            chessboard.transform.Rotate(180, 0, 0);
            chessboard.transform.Rotate(0, 180, 0);

            // Store the initial position and rotation
            initialPosition = chessboard.transform.position;
            initialRotation = chessboard.transform.rotation;
            // Calculate cell size
            cellSize = maxSize / gridSize; // Add this line

            // Clear old cubes
            foreach (var cube in placedCubes)
            {
                Destroy(cube);
            }
            placedCubes.Clear();

            PlaceColoredCells("b1", "c3"); // Modify this line to place an arrow instead of small cubes
        }
    }

    public void PlaceColoredCells(string from, string to) // Modify this method
    {
        // Store the coordinates for later use
        lastFromCell = from;
        lastToCell = to;
        
        Vector2Int fromCoords = ChessNotationToGridCoordinates(from);
        Vector2Int toCoords = ChessNotationToGridCoordinates(to);

        // Calculate positions relative to the board scale
        float scaledCellSize = chessboard.transform.localScale.x / gridSize;
        
        Vector3 fromPosition = chessboard.transform.position +
                              chessboard.transform.right * (fromCoords.x - gridSize/2 + 0.5f) * scaledCellSize +
                              chessboard.transform.forward * (fromCoords.y - gridSize/2 + 0.5f) * scaledCellSize;
                              
        Vector3 toPosition = chessboard.transform.position +
                            chessboard.transform.right * (toCoords.x - gridSize/2 + 0.5f) * scaledCellSize +
                            chessboard.transform.forward * (toCoords.y - gridSize/2 + 0.5f) * scaledCellSize;

        ColorCell(fromPosition, Color.blue);
        ColorCell(toPosition, Color.red);
    }

    private void ColorCell(Vector3 position, Color color) // Modify this method
    {
        GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // Raise the cell position by 0.01 units
        Vector3 raisedPosition = position;
        cell.transform.position = raisedPosition;
        float scaledCellSize = chessboard.transform.localScale.x / gridSize;
        cell.transform.localScale = new Vector3(scaledCellSize, 0.01f, scaledCellSize);
        cell.transform.rotation = chessboard.transform.rotation;
        cell.GetComponent<Renderer>().material.color = color;
        placedCubes.Add(cell);
        cell.transform.SetParent(chessboard.transform, true);
        StartCoroutine(AnimateCell(cell));
    }

    private IEnumerator AnimateCell(GameObject cell) // Modify this method
    {
        // Store initial position which is already raised above the board
        Vector3 startPosition = cell.transform.position;
        float amplitude = 0.004f;
        float frequency = 2f;

        while (true)
        {
            float yOffset = Mathf.Sin(Time.time * frequency) * amplitude;
            // Animate around the raised position
            cell.transform.position = startPosition + new Vector3(0, yOffset, 0);
            yield return null;
        }
    }

    public void PlaceSmallCubes(string from, string to) // Modify this method
    {
        Vector2Int fromCoords = ChessNotationToGridCoordinates(from);
        Vector2Int toCoords = ChessNotationToGridCoordinates(to);

        Vector3 fromPosition = chessboard.transform.position +
                               chessboard.transform.right * (fromCoords.x * cellSize - chessboard.transform.localScale.x / 2 + cellSize / 2) +
                               chessboard.transform.forward * (fromCoords.y * cellSize - chessboard.transform.localScale.z / 2 + cellSize / 2);
        Vector3 toPosition = chessboard.transform.position +
                             chessboard.transform.right * (toCoords.x * cellSize - chessboard.transform.localScale.x / 2 + cellSize / 2) +
                             chessboard.transform.forward * (toCoords.y * cellSize - chessboard.transform.localScale.z / 2 + cellSize / 2);

        GameObject fromCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fromCube.transform.position = fromPosition;
        fromCube.transform.rotation = chessboard.transform.rotation; // Add this line
        fromCube.transform.localScale = new Vector3(cellSize / 2, cellSize / 2, cellSize / 2);
        fromCube.GetComponent<Renderer>().material.color = Color.blue;
        placedCubes.Add(fromCube); // Add this line

        GameObject toCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        toCube.transform.position = toPosition;
        toCube.transform.rotation = chessboard.transform.rotation; // Add this line
        toCube.transform.localScale = new Vector3(cellSize / 2, cellSize / 2, cellSize / 2);
        toCube.GetComponent<Renderer>().material.color = Color.blue;
        placedCubes.Add(toCube); // Add this line
    }

    private Vector2Int ChessNotationToGridCoordinates(string notation) // Add this method
    {
        int file = notation[0] - 'a';
        int rank = int.Parse(notation[1].ToString()) - 1;
        return new Vector2Int(file, rank);
    }

    private void DrawGrid() // Add this method
    {
        for (int i = 0; i <= gridSize; i++)
        {
            // Draw vertical lines
            Vector3 start = chessboard.transform.position +
                            chessboard.transform.right * (i * cellSize - chessboard.transform.localScale.x / 2) +
                            chessboard.transform.forward * (-chessboard.transform.localScale.z / 2);
            Vector3 end = chessboard.transform.position +
                          chessboard.transform.right * (i * cellSize - chessboard.transform.localScale.x / 2) +
                          chessboard.transform.forward * (chessboard.transform.localScale.z / 2);
            Debug.DrawLine(start, end, Color.white, 0.1f);

            // Draw horizontal lines
            start = chessboard.transform.position +
                    chessboard.transform.right * (-chessboard.transform.localScale.x / 2) +
                    chessboard.transform.forward * (i * cellSize - chessboard.transform.localScale.z / 2);
            end = chessboard.transform.position +
                  chessboard.transform.right * (chessboard.transform.localScale.x / 2) +
                  chessboard.transform.forward * (i * cellSize - chessboard.transform.localScale.z / 2);
            Debug.DrawLine(start, end, Color.white, 0.1f);
        }
    }

    // private void DrawCoordinates() // Add this method
    // {
    //     for (int i = 0; i < gridSize; i++)
    //     {
    //         for (int j = 0; j < gridSize; j++)
    //         {
    //             Vector3 position = chessboard.transform.position +
    //                                chessboard.transform.right * (i * cellSize - chessboard.transform.localScale.x / 2 + cellSize / 2) +
    //                                chessboard.transform.forward * (j * cellSize - chessboard.transform.localScale.z / 2 + cellSize / 2);
    //             string coordinate = $"{(char)('a' + i)}{j + 1}";
    //             GUIStyle style = new GUIStyle();
    //             style.normal.textColor = Color.white;
    //             Handles.Label(position, coordinate, style);
    //         }
    //     }
    // }

    // void OnDrawGizmos() // Add this method
    // {
    //     if (chessboard != null && chessboard.activeSelf)
    //     {
    //         DrawGrid();
    //         DrawCoordinates();
    //     }
    // }

    private void DisplayCorners() // Modify this method
    {
        if (points.Count == 4)
        {
            // Sort points based on their coordinates
            points.Sort((a, b) => a.y.CompareTo(b.y)); // Sort by y-coordinate
            if (points[0].x > points[1].x) (points[0], points[1]) = (points[1], points[0]); // Sort top points by x-coordinate
            if (points[2].x > points[3].x) (points[2], points[3]) = (points[3], points[2]); // Sort bottom points by x-coordinate

            // Assign points to corners
            Vector2 topLeft = points[0];
            Vector2 topRight = points[1];
            Vector2 bottomLeft = points[2];
            Vector2 bottomRight = points[3];

            // Convert the point coordinates to screen coordinates
            float topLeftX = topLeft.x / realScreenWidth * Screen.width;
            float topLeftY = topLeft.y / realScreenHeight * Screen.height;
            float topRightX = topRight.x / realScreenWidth * Screen.width;
            float topRightY = topRight.y / realScreenHeight * Screen.height;
            float bottomLeftX = bottomLeft.x / realScreenWidth * Screen.width;
            float bottomLeftY = bottomLeft.y / realScreenHeight * Screen.height;
            float bottomRightX = bottomRight.x / realScreenWidth * Screen.width;
            float bottomRightY = bottomRight.y / realScreenHeight * Screen.height;

            // Draw the corners with different colors
            GUI.color = Color.red; // Top-left
            GUI.DrawTexture(new Rect(topLeftX - 5, topLeftY - 5, 20, 20), Texture2D.whiteTexture);

            GUI.color = Color.green; // Top-right
            GUI.DrawTexture(new Rect(topRightX - 5, topRightY - 5, 20, 20), Texture2D.whiteTexture);

            GUI.color = Color.blue; // Bottom-left
            GUI.DrawTexture(new Rect(bottomLeftX - 5, bottomLeftY - 5, 20, 20), Texture2D.whiteTexture);

            GUI.color = Color.yellow; // Bottom-right
            GUI.DrawTexture(new Rect(bottomRightX - 5, bottomRightY - 5, 20, 20), Texture2D.whiteTexture);
        }
    }

    [System.Serializable]
    private class ResponseData
    {
        public List<Point> corners;
    }

    [System.Serializable]
    private class Point
    {
        public float center_x;
        public float center_y;
    }

    private void ToggleBoard() // Add this method
    {
        isBoardVisible = !isBoardVisible;
        boardRenderer.enabled = isBoardVisible;
    }

    private void UpdateAllColoredCells() // Add this method
    {
        if (!string.IsNullOrEmpty(lastFromCell) && !string.IsNullOrEmpty(lastToCell))
        {
            // Clear existing cells
            foreach (var cube in placedCubes)
            {
                Destroy(cube);
            }
            placedCubes.Clear();
            
            // Recreate cells with new transformations
            PlaceColoredCells(lastFromCell, lastToCell);
        }
    }
}
