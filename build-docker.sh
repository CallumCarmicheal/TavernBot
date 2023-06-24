#!/bin/sh
git pull 
docker build -t cctavern-image -f Dockerfile .
