# bge-micro-v2 (vendored)

Local embedding model. 384 dimensions. Runs in-process via ONNX Runtime, so
transaction descriptions never leave the server and embedding costs nothing per call.

## Why these files are committed

`SmartComponents.LocalEmbeddings` ships no model — its MSBuild targets download one
from HuggingFace at build time. That package was last published in April 2024 and is
unmaintained, so a build-time fetch is an unpinned dependency on a third party staying
up. Vendoring makes builds hermetic: no network at build time, and no chance of a
failed deploy because a HuggingFace URL moved.

The build points at these via `LocalEmbeddingsModelPath` / `LocalEmbeddingsVocabPath`.

## Provenance

Source: https://huggingface.co/SmartComponents/bge-micro-v2 (revision `72908b7`),
itself a fork of https://huggingface.co/TaylorAI/bge-micro-v2.

    model.onnx  <- onnx/model_quantized.onnx
    vocab.txt   <- vocab.txt

## Checksums (verify with `shasum -a 256 -c`)

    ed65e36025aa94cb74207dab863c85452919ec0ab7df3512092932aa22c9a33a  model.onnx
    07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3  vocab.txt
