"""
WSOP-Style Poker — ASGI entry point.

Development:
    uvicorn main:app --reload --port 8000

Production:
    gunicorn main:app -k uvicorn.workers.UvicornWorker -w 4 -b 0.0.0.0:8000

WebSocket endpoint: ws://host:8000/ws
REST endpoints:     http://host:8000/api/...
"""

import logging

from fastapi import FastAPI, WebSocket
from fastapi.middleware.cors import CORSMiddleware

from app.api.routes import router
from app.services.room_manager import RoomManager
from app.sockets.ws_handlers import websocket_endpoint
from app.sockets.ws_manager import WebSocketManager

logging.basicConfig(level=logging.INFO)

app = FastAPI(
    title="Poker Game API",
    description="WSOP-style Texas Hold'em — REST + WebSocket backend",
    version="0.1.0",
)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Shared state — swap RoomManager backing store with Redis for horizontal scaling
room_manager = RoomManager()
ws_manager = WebSocketManager()

app.state.room_manager = room_manager
app.state.ws_manager = ws_manager

app.include_router(router, prefix="/api")


@app.websocket("/ws")
async def ws_route(websocket: WebSocket) -> None:
    await websocket_endpoint(websocket, ws_manager, room_manager)


# Back-compat alias. The pre-WebSocket build used `asgi_app` (a socketio
# ASGIApp wrapper); the Dockerfile and existing run scripts still target that
# name. Keep the alias so `uvicorn main:asgi_app` keeps working alongside the
# canonical `uvicorn main:app`.
asgi_app = app
