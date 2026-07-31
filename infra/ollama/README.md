# Railway Ollama deployment context

Deploy this directory as a **separate private Railway service** with its root set
to `infra/ollama`. Do **not** create a public domain or expose this service to the
internet. The application reaches it only through Railway private networking at
`http://ollama.railway.internal:11434`.

## Required pre-deploy lock step

`model.lock` contains the verified immutable manifest digest for
`all-minilm:l6-v2`, obtained from Ollama 0.30.9 `GET /api/tags` on 2026-07-31.
The entrypoint fails closed unless the installed model has that exact digest. Do not
replace it with a tag or bypass the identity check.

The Docker image is pinned to its verified immutable image digest. Preserve both the
image and model digests unless a replacement has been separately verified.

## Railway setup

1. Create an `ollama` service from this directory and attach a persistent volume at
   `/root/.ollama`.
2. Keep the service private: do not generate a Railway public domain. The only
   health check is the internal `/api/tags` endpoint; it has a 720-second startup
   timeout to allow the first model pull and embedding warm-up.
3. Set this Ollama service variable explicitly (the entrypoint fails closed for any
   missing or different value):
   - `PORT=11434`
4. In the application service, set non-secret variables:
   - `Ollama__BaseUrl=http://ollama.railway.internal:11434`
   - `Ollama__Model=all-minilm:l6-v2`
   - `Ollama__TimeoutSeconds=60`
   - `Ollama__MaxRetries=3`
   - `Rag__EmbeddingDimensions=384`
5. Point the application service liveness check at `/health/live`. `/health/ollama`
   is diagnostic only and intentionally reports unavailable when the dependency is down.

The entrypoint starts Ollama only on loopback while it polls, pulls (only if absent),
checks the locked digest, and performs an actual 384-dimensional non-zero embedding.
It exits non-zero on any bootstrap, pull, identity, or warm-up failure. Bootstrap
cleanup is bounded below 10 seconds; SIGINT returns 130 and SIGTERM returns 143.
After validation it `exec`s the final server so it receives service signals directly.
