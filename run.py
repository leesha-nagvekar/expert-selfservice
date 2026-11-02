"""Start the app: python run.py, then open http://127.0.0.1:8000."""

import uvicorn

# 127.0.0.1 only: the app must never be reachable from the network (ground rule 7).
HOST = "127.0.0.1"
PORT = 8000

if __name__ == "__main__":
    uvicorn.run("app.main:app", host=HOST, port=PORT)
