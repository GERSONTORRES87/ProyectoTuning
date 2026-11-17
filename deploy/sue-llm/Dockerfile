# Modelos de lenguaje locales de SUE (Ollama, solo CPU):
# - qwen2.5:1.5b             responde los chats de la condición AI
# - paraphrase-multilingual  embeddings del filtro RAG (español)
# La imagen oficial trae ~4.5 GB de librerías CUDA/MLX que en Azure (sin GPU) sobran:
# se descargan los modelos en ella y solo se copia lo necesario a una Ubuntu limpia.
FROM ollama/ollama:0.40.12 AS build
RUN (ollama serve >/tmp/ollama.log 2>&1 &) && sleep 8 \
    && ollama pull qwen2.5:1.5b \
    && ollama pull paraphrase-multilingual \
    && ollama list \
    && rm -rf /usr/lib/ollama/cuda_* /usr/lib/ollama/mlx_cuda_* /usr/lib/ollama/vulkan /usr/lib/ollama/include

FROM ubuntu:24.04
COPY --from=build /usr/bin/ollama /usr/bin/ollama
COPY --from=build /usr/lib/ollama /usr/lib/ollama
COPY --from=build /root/.ollama /root/.ollama

# Modelos siempre cargados: la primera respuesta no paga la carga.
ENV OLLAMA_HOST=0.0.0.0:11434 \
    OLLAMA_KEEP_ALIVE=-1 \
    OLLAMA_NUM_PARALLEL=2 \
    OLLAMA_MAX_LOADED_MODELS=2 \
    OLLAMA_CONTEXT_LENGTH=4096
EXPOSE 11434
ENTRYPOINT ["/usr/bin/ollama"]
CMD ["serve"]
