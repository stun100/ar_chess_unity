from fastapi import FastAPI, Request, WebSocket
from pydantic import BaseModel
import base64
from PIL import Image
import io
from datetime import datetime

app = FastAPI()

# Load your ML model here (example placeholder)
# model = load_model()


@app.post("/image")
async def process_image(request: Request):
    try:
        # Read the raw image bytes from the request body
        image_bytes = await request.body()

        # Decode the base64-encoded image
        image = Image.open(io.BytesIO(image_bytes)).convert("RGB")

        # Save the image with a timestamp for debugging purposes
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
        image.save(f"photos/received_image_{timestamp}.jpg")

        # Perform inference with your ML model (example)
        # prediction = model.predict(image)

        # Return a mock prediction response
        response = {
            "prediction": "mock_class",
            "confidence": 0.99
        }
        return response

    except Exception as e:
        return {"error": str(e)}

@app.websocket("/stream")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    try:
        while True:
            data = await websocket.receive_bytes()
            # Decode the base64-encoded image
            image = Image.open(io.BytesIO(data)).convert("RGB")

            # Save the image with a timestamp for debugging purposes
            timestamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
            image.save(f"photos/received_image_{timestamp}.jpg")

            # Perform inference with your ML model (example)
            # prediction = model.predict(image)

            # Return a mock prediction response
            response = {
                "prediction": "mock_class",
                "confidence": 0.99
            }
            await websocket.send_json(response)
    except Exception as e:
        await websocket.send_json({"error": str(e)})
    finally:
        await websocket.close()

