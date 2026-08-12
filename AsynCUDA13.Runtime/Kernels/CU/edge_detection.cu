__global__ void edge_detection(unsigned char* input, unsigned char* output, int width, int height, int edgeR, int edgeG, int edgeB, int thickness, float threshold)
{
	int i = blockIdx.x * blockDim.x + threadIdx.x;

	// Bildgrenzen prüfen (jedes Pixel besteht aus 3 Werten: R, G, B)
	int pixelIdx = i * 3;
	int totalPixels = width * height * 3;

	if (pixelIdx + 2 >= totalPixels) return;

	// Schwellwert clammen (0.0 = alle Kanten, 1.0 = nur starke Kanten)
	float t = fmaxf(0.0f, fminf(1.0f, threshold));

	// Sobel-Filter-Komponenten
	float gx_r = 0.0f, gy_r = 0.0f;
	float gx_g = 0.0f, gy_g = 0.0f;
	float gx_b = 0.0f, gy_b = 0.0f;

	// Sobel-Kernel für Gradientenberechnung
	int x = i % width;
	int y = i / width;

	// Kantenpixel zählen
	int edgePixels = 0;

	// 3x3 Nachbarschaft prüfen
	for (int dy = -1; dy <= 1; dy++) {
		for (int dx = -1; dx <= 1; dx++) {
			int nx = x + dx;
			int ny = y + dy;

			// Grenzen prüfen
			if (nx >= 0 && nx < width && ny >= 0 && ny < height) {
				int neighborIdx = (ny * width + nx) * 3;

				// Sobel-Gitter
				int sobelX = dx;
				int sobelY = dy;

				// Grauwerte berechnen (einfacher Graustufen-Ansatz)
				float gray_in = 0.299f * input[neighborIdx] + 0.587f * input[neighborIdx + 1] + 0.114f * input[neighborIdx + 2];

				// Gradienten accumulieren
				gx_r += sobelX * gray_in;
				gy_r += sobelY * gray_in;
				gx_g += sobelX * gray_in;
				gy_g += sobelY * gray_in;
				gx_b += sobelX * gray_in;
				gy_b += sobelY * gray_in;
			}
		}
	}

	// Gradientenmagnetude berechnen
	float mag_r = sqrtf(gx_r * gx_r + gy_r * gy_r);
	float mag_g = sqrtf(gx_g * gx_g + gy_g * gy_g);
	float mag_b = sqrtf(gx_b * gx_b + gy_b * gy_b);

	// Durchschnittliche Magentude
	float magnitude = (mag_r + mag_g + mag_b) / 3.0f;

	// Normalisieren und mit Schwellwert vergleichen
	float maxMag = 255.0f * sqrtf(2.0f); // Maximale Sobel-Magentude
	float normalizedMag = magnitude / maxMag;

	// Kanten erkennen
	bool isEdge = normalizedMag > t;

	if (isEdge) {
		// Kantenfarbe anwenden
		output[pixelIdx] = edgeR;     // Rot
		output[pixelIdx + 1] = edgeG; // Grün
		output[pixelIdx + 2] = edgeB; // Blau
	} else {
		// Originalpixel übernehmen
		output[pixelIdx] = input[pixelIdx];
		output[pixelIdx + 1] = input[pixelIdx + 1];
		output[pixelIdx + 2] = input[pixelIdx + 2];
	}

	// Kantenstiftdicke (thickness) - hier wird die Dicke durch wiederholte Kanten pixelweise simuliert
	// Für echte Dicke würde man mehrere Kernel-Läufe oder erweiterte Logik benötigen
	if (thickness > 1 && isEdge) {
		// Nahe benachbarte Pixel mit Kantenfarbe überschreiben
		for (int t_idx = 1; t_idx < thickness; t_idx++) {
			int offsetX = (i + t_idx * width) * 3;
			if (offsetX + 2 < totalPixels) {
				output[offsetX] = edgeR;
				output[offsetX + 1] = edgeG;
				output[offsetX + 2] = edgeB;
			}
		}
	}
}