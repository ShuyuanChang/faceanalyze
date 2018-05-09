import sys
import os
import numpy as np
import cv2
from datetime import datetime
from azure.storage.blob import BlockBlobService
from azure.storage.blob import PublicAccess
from azure.storage.blob import ContentSettings
import http.client, urllib.request, urllib.parse, urllib.error, base64, sys, json


# faceCascade = cv2.CascadeClassifier(cascPath)
faceCascade = cv2.CascadeClassifier('./data/haarcascades/haarcascade_frontalface_default.xml')
eye_cascade = cv2.CascadeClassifier('./data/haarcascades/haarcascade_eye.xml')
a = 0
a = int (input("which webcam:"))
video_capture = cv2.VideoCapture(a)

# Toggles Rectangle and Debug logs
debug = False
# debug = True
emotion =  "Happy"
block_blob_service = BlockBlobService(account_name='[Your storage account name]', account_key='[Your storage account key]')

def runFeed():         

    while True:
        # Capture frame-by-frame
        ret, frame = video_capture.read()
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = faceCascade.detectMultiScale(gray, 1.3, 5)
  
        for (x,y,w,h) in faces:

            frameClone = frame.copy()
            timestr = datetime.utcnow().strftime("%Y%m%d-%H%M%S%f")
            cv2.rectangle(frame,(x,y),(x+w,y+h),(0,0,0),2)
            cv2.imwrite('./log/face'+timestr+'.png',frameClone)
            print("A face detected!")
            block_blob_service.create_blob_from_path(
                    'frames',
                    'face'+timestr+'.png',
                    './log/face'+timestr+'.png',
                    content_settings=ContentSettings(content_type='image/png')
                    )
            os.remove('./log/face'+timestr+'.png')


        # Display the resulting frame
        cv2.imshow('Video', frame)




        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

# When everything is done, release the capture
    video_capture.release()
    cv2.destroyAllWindows()
