FROM python:3.12-slim
RUN pip install --no-cache-dir platformio==6.1.18
WORKDIR /firmware
COPY hardware/HardwareTest/ ./
RUN pio run -e mkrwifi1010
CMD ["pio", "run", "-e", "mkrwifi1010"]
