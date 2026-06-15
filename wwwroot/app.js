const form = document.querySelector("#embed-form");
const iframe = document.querySelector("#sigma-iframe");
const statusBox = document.querySelector("#status");

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  await loadSigmaEmbed(new FormData(form));
});

async function loadSigmaEmbed(formData) {
  const params = new URLSearchParams(formData);
  statusBox.textContent = "Requesting signed Sigma embed URL...";

  try {
    const response = await fetch(`/api/sigma/embed-url?${params.toString()}`);
    const body = await response.json();

    if (!response.ok) {
      throw new Error(body?.detail || body?.title || "Could not create embed URL.");
    }

    iframe.src = body.embedUrl;
    statusBox.textContent = `Loaded. JWT expires at ${new Date(body.expiresAt).toLocaleString()}.`;
  } catch (error) {
    iframe.removeAttribute("src");
    statusBox.textContent = error instanceof Error ? error.message : String(error);
  }
}

loadSigmaEmbed(new FormData(form));

