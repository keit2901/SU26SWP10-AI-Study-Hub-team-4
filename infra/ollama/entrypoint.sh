#!/bin/sh
# Bootstraps the pinned embedding model on localhost, then starts the public
# Railway listener only after the exact locked model has passed a real embedding.
set -eu

LOCK_FILE=/opt/aistudy/model.lock
BOOTSTRAP_HOST=127.0.0.1:11434
REQUIRED_PORT=11434
BOOTSTRAP_TIMEOUT_SECONDS=60
PULL_TIMEOUT_SECONDS=180
PULL_ATTEMPTS=3
WARMUP_TIMEOUT_SECONDS=30

if [ "${PORT:-}" != "$REQUIRED_PORT" ]; then
    echo "PORT must be explicitly set to ${REQUIRED_PORT}; refusing to expose Ollama on a different port." >&2
    exit 1
fi

if [ ! -r "$LOCK_FILE" ]; then
    echo "Missing model lock file: $LOCK_FILE" >&2
    exit 1
fi

EXPECTED_MODEL=$(sed -n 's/^MODEL=//p' "$LOCK_FILE")
EXPECTED_DIGEST=$(sed -n 's/^MANIFEST_DIGEST=//p' "$LOCK_FILE")
if [ -z "$EXPECTED_MODEL" ] || [ -z "$EXPECTED_DIGEST" ]; then
    echo "model.lock must define MODEL and MANIFEST_DIGEST." >&2
    exit 1
fi
if [ "$EXPECTED_MODEL" != "all-minilm:l6-v2" ]; then
    echo "Refusing non-approved embedding model '$EXPECTED_MODEL'." >&2
    exit 1
fi
case "$EXPECTED_DIGEST" in
    sha256:*) ;;
    *)
        echo "model.lock has no verified immutable manifest digest; refusing tag-only deployment." >&2
        exit 1
        ;;
esac

bootstrap_pid=""
cleanup_bootstrap() {
    if [ -z "$bootstrap_pid" ] || ! kill -0 "$bootstrap_pid" 2>/dev/null; then
        bootstrap_pid=""
        return
    fi

    kill -TERM "$bootstrap_pid" 2>/dev/null || true

    # Never let failure or an interrupt leave the bootstrap cleanup blocking.
    # Three one-second polls plus SIGKILL leave room under the ten-second
    # bootstrap cleanup budget even when a foreground probe is unwinding.
    cleanup_attempt=1
    while kill -0 "$bootstrap_pid" 2>/dev/null && [ "$cleanup_attempt" -le 3 ]; do
        sleep 1
        cleanup_attempt=$((cleanup_attempt + 1))
    done
    if kill -0 "$bootstrap_pid" 2>/dev/null; then
        kill -KILL "$bootstrap_pid" 2>/dev/null || true
    fi
    bootstrap_pid=""
}

# EXIT invokes the bounded cleanup. The checks run under a background worker so
# PID 1 can process an interrupt between one-second polls rather than waiting on
# a foreground curl or model pull.
bootstrap_checks_pid=""
handle_signal() {
    signal_exit_code=$1
    if [ -n "$bootstrap_checks_pid" ] && kill -0 "$bootstrap_checks_pid" 2>/dev/null; then
        kill -KILL "$bootstrap_checks_pid" 2>/dev/null || true
    fi
    cleanup_bootstrap
    trap - EXIT INT TERM
    exit "$signal_exit_code"
}
trap cleanup_bootstrap EXIT
trap 'handle_signal 130' INT
trap 'handle_signal 143' TERM

OLLAMA_HOST="$BOOTSTRAP_HOST" ollama serve &
bootstrap_pid=$!

bootstrap_checks() {
    trap - EXIT INT TERM

deadline=$(( $(date +%s) + BOOTSTRAP_TIMEOUT_SECONDS ))
until curl --silent --show-error --fail --connect-timeout 3 --max-time 5 "http://${BOOTSTRAP_HOST}/api/tags" >/dev/null; do
    if [ "$(date +%s)" -ge "$deadline" ]; then
        echo "Ollama bootstrap did not become ready within ${BOOTSTRAP_TIMEOUT_SECONDS}s." >&2
        exit 1
    fi
    sleep 1
done

model_is_present() {
    curl --silent --show-error --fail --connect-timeout 3 --max-time 5 "http://${BOOTSTRAP_HOST}/api/tags" \
        | jq -e --arg model "$EXPECTED_MODEL" '.models[]? | select(.name == $model)' >/dev/null
}

if ! model_is_present; then
    attempt=1
    while [ "$attempt" -le "$PULL_ATTEMPTS" ]; do
        echo "Pulling locked model ${EXPECTED_MODEL} (attempt ${attempt}/${PULL_ATTEMPTS})."
        if timeout "$PULL_TIMEOUT_SECONDS" ollama pull "$EXPECTED_MODEL" && model_is_present; then
            break
        fi
        if [ "$attempt" -eq "$PULL_ATTEMPTS" ]; then
            echo "Failed to pull ${EXPECTED_MODEL} after ${PULL_ATTEMPTS} bounded attempts." >&2
            exit 1
        fi
        attempt=$((attempt + 1))
        sleep 2
    done
fi

actual_digest=$(curl --silent --show-error --fail --connect-timeout 3 --max-time 5 "http://${BOOTSTRAP_HOST}/api/tags" \
    | jq -r --arg model "$EXPECTED_MODEL" '.models[]? | select(.name == $model) | .digest' \
    | head -n 1)
case "$actual_digest" in
    sha256:*) ;;
    [0-9a-fA-F][0-9a-fA-F]*) actual_digest="sha256:${actual_digest}" ;;
esac
if [ "$actual_digest" != "$EXPECTED_DIGEST" ]; then
    echo "Model digest mismatch for ${EXPECTED_MODEL}; expected ${EXPECTED_DIGEST}, got ${actual_digest:-missing}." >&2
    exit 1
fi

warmup_json=$(curl --silent --show-error --fail --connect-timeout 5 --max-time "$WARMUP_TIMEOUT_SECONDS" \
    --header 'Content-Type: application/json' \
    --data "{\"model\":\"${EXPECTED_MODEL}\",\"prompt\":\"AI Study Hub embedding warm-up\"}" \
    "http://${BOOTSTRAP_HOST}/api/embeddings")
if ! printf '%s' "$warmup_json" | jq -e '.embedding | length == 384 and any(.[]; . != 0)' >/dev/null; then
    echo "Embedding warm-up did not return a non-zero 384-dimension vector." >&2
    exit 1
fi
}

bootstrap_checks &
bootstrap_checks_pid=$!
while kill -0 "$bootstrap_checks_pid" 2>/dev/null; do
    sleep 1
done
if ! wait "$bootstrap_checks_pid"; then
    exit 1
fi
bootstrap_checks_pid=""

cleanup_bootstrap
trap - EXIT INT TERM

export OLLAMA_HOST="0.0.0.0:${REQUIRED_PORT}"
exec ollama serve
