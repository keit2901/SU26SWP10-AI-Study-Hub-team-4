# Immutable image verified by the manual deployment smoke on 2026-07-31.
FROM ollama/ollama:0.30.9@sha256:1a72f129b5f18e4552a15fb9356c23c16513aa54400b9538897990fb597b5df2

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl jq \
    && rm -rf /var/lib/apt/lists/*

COPY model.lock /opt/aistudy/model.lock
COPY entrypoint.sh /usr/local/bin/aistudy-ollama-entrypoint
RUN sed -i 's/\r$//' /opt/aistudy/model.lock /usr/local/bin/aistudy-ollama-entrypoint \
    && chmod 0755 /usr/local/bin/aistudy-ollama-entrypoint

VOLUME ["/root/.ollama"]
ENV PORT=11434
EXPOSE 11434

ENTRYPOINT ["/usr/local/bin/aistudy-ollama-entrypoint"]
