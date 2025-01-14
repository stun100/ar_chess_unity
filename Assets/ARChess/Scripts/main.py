import json
from fastapi import FastAPI, HTTPException, Request, WebSocket
from pydantic import BaseModel
import base64
from PIL import Image
import io
from datetime import datetime
from ultralytics import YOLO  # Import YOLO
from fastapi.responses import JSONResponse
from chesboard_inference import chessboard_inference

app = FastAPI()

# Load your ML model here (example placeholder)
model = YOLO("best.pt")

# Define class names
class_names = {
    0: "white-pawn",
    1: "white-rook",
    2: "white-knight",
    3: "white-bishop",
    4: "white-queen",
    5: "white-king",
    6: "black-pawn",
    7: "black-rook",
    8: "black-knight",
    9: "black-bishop",
    10: "black-queen",
    11: "black-king",
    12: "corner",
    13: "empty"
}


@app.websocket("/calibration")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    try:
        while True:
            data = await websocket.receive_bytes()
            # Decode the base64-encoded image
            image = Image.open(io.BytesIO(data)).convert("RGB")

            # Rotate the image to the left by 90 degrees
            image = image.rotate(90, expand=True)

            # Center the crop so that the width is 296
            width, height = image.size
            if width > 296:
                left = (width - 296) // 2
                right = left + 296
                image = image.crop((left, 0, right, height))

            # # Save the image with a timestamp for debugging purposes
            # timestamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
            # image.save(f"photos/received_image_{timestamp}.jpg")

            # Perform inference with your ML model (example)
            prediction = model(image)
            bboxes = []
            for result in prediction:
                for box, cls in zip(result.boxes.xyxy.cpu().numpy().tolist(),
                                    result.boxes.cls.cpu().numpy().tolist()):
                    class_name = class_names.get(int(cls), "unknown")
                    if class_name == "corner":
                        center_x = (box[0] + box[2]) / 2
                        center_y = (box[1] + box[3]) / 2
                        bboxes.append({
                            "center_x": center_x,
                            "center_y": center_y
                        })

            # Only send the response if there are at least 4 corners

            response = json.dumps({"corners": bboxes})
            await websocket.send_text(response)

    except Exception as e:
        await websocket.send_json({"error": str(e)})
    finally:
        await websocket.close()

# test endpoint


@app.websocket("/calibration_unity")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    try:
        while True:
            data = await websocket.receive_bytes()
            # Decode the base64-encoded image
            image = Image.open(io.BytesIO(data)).convert("RGB")

            # Mirror the image on the y-axis
            image = image.transpose(Image.FLIP_LEFT_RIGHT)

            # # Save the image with a timestamp for debugging purposes
            # timestamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
            # image.save(f"photos/received_image_{timestamp}.jpg")

            # Perform inference with your ML model (example)
            prediction = model(image)
            bboxes = []
            for result in prediction:
                for box, cls in zip(result.boxes.xyxy.cpu().numpy().tolist(),
                                    result.boxes.cls.cpu().numpy().tolist()):
                    class_name = class_names.get(int(cls), "unknown")
                    if class_name == "corner":
                        center_x = (box[0] + box[2]) / 2
                        center_y = (box[1] + box[3]) / 2
                        bboxes.append({
                            "center_x": center_x,
                            "center_y": center_y
                        })

            # Only send the response if there are at least 4 corners

            response = json.dumps({"corners": bboxes})
            await websocket.send_text(response)

    except Exception as e:
        await websocket.send_json({"error": str(e)})
    finally:
        await websocket.close()


class ChessMoveRequest(BaseModel):
    image_data: str
    bottom_left: list
    bottom_right: list
    top_left: list
    top_right: list
    player_turn: str


@app.post("/chess_move")
async def http_chess_move(request: ChessMoveRequest):
    try:
        image_data = request.image_data
        bottom_left = request.bottom_left
        bottom_right = request.bottom_right
        top_left = request.top_left
        top_right = request.top_right
        player_turn = request.player_turn

        corners = {
            "bottom_left": bottom_left,
            "bottom_right": bottom_right,
            "top_left": top_left,
            "top_right": top_right
        }

        # Log the corners
        print(f"Received corners: {corners}")

        # Decode the base64-encoded image
        image_data = base64.b64decode(image_data)
        image = Image.open(io.BytesIO(image_data)).convert("RGB")

        # Mirror the image on the y-axis
        image = image.transpose(Image.FLIP_LEFT_RIGHT)

        # Save the image with a timestamp for debugging purposes
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
        image.save(f"photos/received_image_{timestamp}.jpg")

        results = model(image)

        turn, start_pos, end_pos, fen = chessboard_inference(
            results, corners, player_turn)

        response = {
            "turn": turn,
            "start_pos": start_pos,
            "end_pos": end_pos,
            "fen": fen
        }

        return response

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
